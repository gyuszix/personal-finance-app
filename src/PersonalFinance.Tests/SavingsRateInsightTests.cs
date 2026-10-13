using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Tests;

// Savings rate (#91).
public class SavingsRateInsightTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static readonly DateTime ThisMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SavingsRate_IsTheShareOfIncomeNotSpent()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, -2000m, category: "INCOME");
        await user.AddTransactionAsync(account, 1500m, category: "RENT_AND_UTILITIES");
        await user.AddTransactionAsync(account, 900m, category: "TRANSFER_OUT");

        var rate = await user.Client.GetFromJsonAsync<SavingsRateResponse>("/api/v1/insights/savings-rate");

        Assert.Equal(2000m, rate!.Income);
        Assert.Equal(1500m, rate.Spending);
        Assert.Equal(0.25m, rate.SavingsRate);
        Assert.Equal(12, rate.Months.Count);
        Assert.Equal(0.25m, rate.Months[^1].SavingsRate);
    }

    [Fact]
    public async Task SavingsRate_IsNegative_WhenMoreWasSpentThanEarned()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, -1000m, category: "INCOME");
        await user.AddTransactionAsync(account, 1500m, category: "TRAVEL");

        var rate = await user.Client.GetFromJsonAsync<SavingsRateResponse>("/api/v1/insights/savings-rate");

        Assert.Equal(-0.5m, rate!.SavingsRate);
    }

    [Fact]
    public async Task SavingsRate_WithNoIncome_IsNull()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 300m, category: "TRAVEL");
        await user.AddTransactionAsync(account, -500m, category: "INCOME", date: ThisMonth.AddMonths(-1));

        var rate = await user.Client.GetFromJsonAsync<SavingsRateResponse>("/api/v1/insights/savings-rate");

        Assert.Null(rate!.SavingsRate);
        Assert.Equal(1m, rate.Months[^2].SavingsRate);
        Assert.Null(rate.Months[0].SavingsRate);
    }

    [Fact]
    public async Task SavingsRate_OnlyUsesTheCallersData()
    {
        var alice = await TestUser.CreateAsync(factory);
        var account = await alice.AddAccountAsync();
        await alice.AddTransactionAsync(account, -1000m, category: "INCOME");
        var bob = await TestUser.CreateAsync(factory);

        var rate = await bob.Client.GetFromJsonAsync<SavingsRateResponse>("/api/v1/insights/savings-rate");

        Assert.Equal(0m, rate!.Income);
        Assert.Null(rate.SavingsRate);
    }
}
