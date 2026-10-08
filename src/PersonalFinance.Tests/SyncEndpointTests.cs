using System.Net;
using System.Net.Http.Json;

namespace PersonalFinance.Tests;

public class SyncEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private record SyncResult(int Added, int Modified, int Removed, List<int> FailedAccountIds);

    [Fact]
    public async Task Sync_WithNoAccounts_ReturnsOk()
    {
        var user = await TestUser.CreateAsync(factory);

        var response = await user.Client.PostAsync("/api/v1/transactions/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SyncResult>();
        Assert.Empty(result!.FailedAccountIds);
    }

    // The seeded access tokens were never encrypted by Data Protection, so
    // each Item fails the way a lost key ring or a dead Plaid Item would.
    // Every Item must still be attempted and reported, not just the first.
    [Fact]
    public async Task Sync_WhenEveryItemFails_ReportsAllOfThemWith502()
    {
        var user = await TestUser.CreateAsync(factory);
        var first = await user.AddAccountAsync(accessToken: "undecryptable-a");
        var second = await user.AddAccountAsync(accessToken: "undecryptable-b");

        var response = await user.Client.PostAsync("/api/v1/transactions/sync", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SyncResult>();
        Assert.Equivalent(new[] { first.AccountId, second.AccountId }, result!.FailedAccountIds);
    }
}
