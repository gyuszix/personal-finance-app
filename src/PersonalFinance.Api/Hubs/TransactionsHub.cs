using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PersonalFinance.Api.Hubs;

// Pushes sync-completed notifications to connected clients, so a client
// doesn't have to poll to find out new transactions/balances landed.
// No client-invokable methods - this is server -> client only, fired from
// SyncNotifier after a manual (/transactions/sync) or scheduled
// (ScheduledPlaidSyncService) sync completes.
[Authorize]
public class TransactionsHub : Hub
{
    private readonly ILogger<TransactionsHub> _logger;

    public TransactionsHub(ILogger<TransactionsHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "Client connected to TransactionsHub: {UserId}", Context.UserIdentifier);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected from TransactionsHub: {UserId}", Context.UserIdentifier);
        return base.OnDisconnectedAsync(exception);
    }
}
