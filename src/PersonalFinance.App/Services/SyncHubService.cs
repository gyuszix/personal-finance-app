using CommunityToolkit.Mvvm.Messaging;
using Microsoft.AspNetCore.SignalR.Client;

namespace PersonalFinance.App.Services;

// What the API's SyncNotifier pushes after a manual or scheduled sync.
public record SyncCompletedMessage(int Added, int Modified, int Removed)
{
    public bool HasTransactionChanges => Added + Modified + Removed > 0;
}

// Listens on the API's /hubs/transactions SignalR hub for the whole signed-in
// session and rebroadcasts SyncCompleted through WeakReferenceMessenger, so
// pages pick up the scheduled background sync without the user tapping
// Refresh. Best effort: if the hub can't be reached the app just behaves as
// before (manual Refresh still works).
public class SyncHubService(AuthTokenProvider tokenProvider)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private HubConnection? _connection;

    public async Task StartAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_connection != null) return;

            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(new Uri(ApiConfig.BaseUrl), "/hubs/transactions"), options =>
                {
                    // Read on every (re)connect, so a reconnect after the
                    // access token was refreshed uses the new one.
                    options.AccessTokenProvider = () => Task.FromResult(tokenProvider.AccessToken);
                })
                .WithAutomaticReconnect()
                .Build();

            connection.On<SyncCompletedMessage>("SyncCompleted",
                message => WeakReferenceMessenger.Default.Send(message));

            await connection.StartAsync();
            _connection = connection;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncHub] Couldn't connect: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_connection == null) return;
            await _connection.DisposeAsync();
            _connection = null;
        }
        finally
        {
            _lock.Release();
        }
    }
}
