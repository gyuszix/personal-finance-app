using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Services;

// Runs PlaidSyncService against every linked account on a schedule, instead
// of only syncing when a client calls POST /transactions/sync.
public class ScheduledPlaidSyncService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<ScheduledPlaidSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = config.GetValue("Sync:IntervalMinutes", 30);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        // Run once immediately on startup, then on the configured interval.
        do
        {
            try
            {
                await RunSyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled Plaid sync run failed");
            }
        } while (!stoppingToken.IsCancellationRequested
                 && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunSyncAsync(CancellationToken stoppingToken)
    {
        // BackgroundService runs outside any HTTP request, so a fresh DI
        // scope is needed per run - AppDbContext, PlaidSyncService, and
        // SummaryCache are all scoped/request-lifetime services.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var syncService = scope.ServiceProvider.GetRequiredService<PlaidSyncService>();
        var summaryCache = scope.ServiceProvider.GetRequiredService<SummaryCache>();
        var syncNotifier = scope.ServiceProvider.GetRequiredService<SyncNotifier>();

        // No HttpContext here, so AppDbContext's per-user query filter
        // (_currentUserId, derived from IHttpContextAccessor) naturally
        // resolves to null - which the filter treats as "no restriction" -
        // so this already returns every account across every user, no
        // IgnoreQueryFilters() needed.
        var accounts = await db.Accounts.ToListAsync(stoppingToken);

        // Aggregate counts per user, same shape the manual sync endpoint
        // reports, so both notify connected clients identically.
        var totalsByUser = new Dictionary<string, (int Added, int Modified, int Removed)>();

        foreach (var account in accounts)
        {
            try
            {
                var (added, modified, removed) = await syncService.SyncAccountAsync(account);
                var current = totalsByUser.GetValueOrDefault(account.UserId);
                totalsByUser[account.UserId] = (
                    current.Added + added,
                    current.Modified + modified,
                    current.Removed + removed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled sync failed for account {AccountId}", account.AccountId);
            }
        }

        foreach (var (userId, totals) in totalsByUser)
        {
            summaryCache.InvalidateForUser(userId);
            await syncNotifier.NotifySyncCompletedAsync(userId, totals.Added, totals.Modified, totals.Removed);
        }

        logger.LogInformation(
            "Scheduled sync completed: {AccountCount} accounts across {UserCount} users",
            accounts.Count, totalsByUser.Count);
    }
}
