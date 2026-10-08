using System.Net;
using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Tests;

public class TransactionEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Delete_InvalidatesCachedSummary()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 10m);
        var doomed = await user.AddTransactionAsync(account, 25m);

        // Prime the cache.
        var before = await user.Client.GetFromJsonAsync<List<TransactionSummaryResponse>>("/api/v1/transactions/summary");
        Assert.Equal(35m, before!.Single().Total);

        var delete = await user.Client.DeleteAsync($"/api/v1/transactions/{doomed.TransactionId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var after = await user.Client.GetFromJsonAsync<List<TransactionSummaryResponse>>("/api/v1/transactions/summary");
        Assert.Equal(10m, after!.Single().Total);
    }
}
