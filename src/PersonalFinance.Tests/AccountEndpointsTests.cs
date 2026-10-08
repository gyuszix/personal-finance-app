using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Tests;

public class AccountEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Summary_SplitsAssetsAndLiabilities()
    {
        var user = await TestUser.CreateAsync(factory);
        await user.AddAccountAsync(balance: 1500m, accountType: "Depository");
        await user.AddAccountAsync(balance: 500m, accountType: "Investment");
        await user.AddAccountAsync(balance: 400m, accountType: "Credit");

        var summary = await user.Client.GetFromJsonAsync<AccountsSummaryResponse>("/api/v1/accounts/summary");

        Assert.Equal(3, summary!.Accounts.Count);
        Assert.Equal(2000m, summary.TotalAssets);
        Assert.Equal(400m, summary.TotalLiabilities);
        Assert.Equal(1600m, summary.NetWorth);
    }
}
