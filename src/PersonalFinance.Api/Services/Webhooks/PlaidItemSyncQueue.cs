using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Services.Webhooks;

// Item syncs requested by webhooks, worked off in the background so the
// webhook response stays fast - Plaid retries a webhook that doesn't get a
// 200 within 10 seconds (#118). An Item already waiting isn't queued twice:
// one sync picks up everything Plaid has. Concurrency with the schedule or a
// manual sync is handled by PlaidSyncService's row lock (#114).
public class PlaidItemSyncQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    // False if the Item was already waiting.
    public bool Enqueue(string itemId) =>
        _pending.TryAdd(itemId, 0) && _channel.Writer.TryWrite(itemId);

    public async IAsyncEnumerable<string> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var itemId in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            // Cleared before the sync starts: a webhook arriving during it
            // queues another run, which picks up anything this one missed.
            _pending.TryRemove(itemId, out _);
            yield return itemId;
        }
    }
}

public class PlaidWebhookSyncWorker(
    PlaidItemSyncQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PlaidWebhookSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var itemId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SyncAsync(itemId, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Webhook-triggered sync failed for Plaid Item {ItemId}", itemId);
            }
        }
    }

    private async Task SyncAsync(string itemId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // No signed-in user here; the Item decides whose accounts these are.
        var accounts = await db.Accounts.IgnoreQueryFilters()
            .Where(a => a.PlaidItemId == itemId)
            .ToListAsync(cancellationToken);
        if (accounts.Count == 0)
        {
            logger.LogWarning("Webhook for Plaid Item {ItemId}, which has no accounts here", itemId);
            return;
        }

        var sync = scope.ServiceProvider.GetRequiredService<PlaidSyncService>();
        var userId = accounts[0].UserId;
        var (added, modified, removed) = await sync.SyncItemAsync(accounts);

        scope.ServiceProvider.GetRequiredService<SummaryCache>().InvalidateForUser(userId);
        await scope.ServiceProvider.GetRequiredService<SyncNotifier>().NotifySyncCompletedAsync(userId, added, modified, removed);
        logger.LogInformation("Webhook-triggered sync of Plaid Item {ItemId}: {Added} added, {Modified} modified, {Removed} removed",
            itemId, added, modified, removed);
    }
}
