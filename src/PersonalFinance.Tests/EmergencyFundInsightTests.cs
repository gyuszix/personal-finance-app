using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Tests;

// Emergency fund coverage (#90).
public class EmergencyFundInsightTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static readonly DateTime LastMonth =
        new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);

    [Fact]
    public async Task EmergencyFund_DividesLiquidCashByTheBurnRate()
    {
        var user = await TestUser.CreateAsync(factory);
        var checking = await user.AddAccountAsync(balance: 2000m, accountType: "Depository", subtype: "Checking");
        await user.AddAccountAsync(balance: 4000m, accountType: "Depository", subtype: "Savings");
        await user.AddAccountAsync(balance: 1000m, accountType: "Depository");   // no subtype yet: liquid
        // Not liquid:
        await user.AddAccountAsync(balance: 9000m, accountType: "Depository", subtype: "Cd");
        await user.AddAccountAsync(balance: 50000m, accountType: "Investment", subtype: "Brokerage");
        await user.AddAccountAsync(balance: 500m, accountType: "Credit", subtype: "CreditCard");
        // 6,000 over three months = a 2,000 burn rate
        await user.AddTransactionAsync(checking, 6000m, date: LastMonth.AddDays(1));

        var fund = await user.Client.GetFromJsonAsync<EmergencyFundResponse>("/api/v1/insights/emergency-fund");

        Assert.Equal(7000m, fund!.LiquidCash);
        Assert.Equal(3, fund.LiquidAccounts);
        Assert.Equal(2000m, fund.MonthlyBurnRate);
        Assert.Equal(3.5m, fund.MonthsCovered);
    }

    [Fact]
    public async Task EmergencyFund_WithNoSpendingHistory_HasNoCoverageFigure()
    {
        var user = await TestUser.CreateAsync(factory);
        await user.AddAccountAsync(balance: 2000m, subtype: "Checking");

        var fund = await user.Client.GetFromJsonAsync<EmergencyFundResponse>("/api/v1/insights/emergency-fund");

        Assert.Equal(2000m, fund!.LiquidCash);
        Assert.Null(fund.MonthsCovered);
    }

    [Fact]
    public async Task EmergencyFund_OnlyUsesTheCallersAccounts()
    {
        var alice = await TestUser.CreateAsync(factory);
        await alice.AddAccountAsync(balance: 2000m, subtype: "Checking");
        var bob = await TestUser.CreateAsync(factory);

        var fund = await bob.Client.GetFromJsonAsync<EmergencyFundResponse>("/api/v1/insights/emergency-fund");

        Assert.Equal(0m, fund!.LiquidCash);
        Assert.Equal(0, fund.LiquidAccounts);
    }
}
