using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace PersonalFinance.Tests;

// The app listens for SyncCompleted to refresh itself after background syncs
// (#62) - pin down that the hub accepts its token and delivers the message,
// in the shape the app deserializes, to the user whose data synced.
public class SyncHubTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    // Mirrors PersonalFinance.App.Services.SyncCompletedMessage.
    private record SyncCompletedMessage(int Added, int Modified, int Removed);

    [Fact]
    public async Task ManualSync_PushesSyncCompletedToThatUser()
    {
        var user = await TestUser.CreateAsync(factory);
        var bystander = await TestUser.CreateAsync(factory);

        await using var userHub = await ConnectAsync(user);
        await using var bystanderHub = await ConnectAsync(bystander);

        var received = new TaskCompletionSource<SyncCompletedMessage>();
        userHub.On<SyncCompletedMessage>("SyncCompleted", m => received.TrySetResult(m));
        var bystanderGotOne = false;
        bystanderHub.On<SyncCompletedMessage>("SyncCompleted", _ => bystanderGotOne = true);

        (await user.Client.PostAsync("/api/v1/transactions/sync", null)).EnsureSuccessStatusCode();

        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new SyncCompletedMessage(0, 0, 0), message);
        Assert.False(bystanderGotOne);
    }

    private async Task<HubConnection> ConnectAsync(TestUser user)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/transactions"), options =>
            {
                // TestServer has no real sockets - long polling over its handler.
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(user.AccessToken);
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }
}
