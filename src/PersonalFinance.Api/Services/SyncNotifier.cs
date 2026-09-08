using Microsoft.AspNetCore.SignalR;
using PersonalFinance.Api.Hubs;

namespace PersonalFinance.Api.Services;

// Pushes a "sync just completed" notification to a specific user's
// connected clients (if any) via SignalR. Called from both the manual
// POST /transactions/sync endpoint and ScheduledPlaidSyncService - one
// place owns the message shape so both stay in sync.
public class SyncNotifier(IHubContext<TransactionsHub> hubContext)
{
    public Task NotifySyncCompletedAsync(string userId, int added, int modified, int removed) =>
        hubContext.Clients.User(userId).SendAsync("SyncCompleted", new
        {
            added,
            modified,
            removed
        });
}
