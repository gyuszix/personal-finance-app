using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Tests;

// Burn rate (#92).
public class BurnRateInsightTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static readonly DateTime ThisMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task BurnRate_AveragesTheThreeCompleteMonthsBeforeThisOne()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 300m, date: ThisMonth.AddMonths(-1).AddDays(1));
        await user.AddTransactionAsync(account, 600m, date: ThisMonth.AddMonths(-2).AddDays(1));
        await user.AddTransactionAsync(account, 900m, date: ThisMonth.AddMonths(-3).AddDays(1));
        // Outside the window: this (incomplete) month and four months back
        await user.AddTransactionAsync(account, 5000m, date: ThisMonth.AddMonths(-4).AddDays(1));
        await user.AddTransactionAsync(account, 100m, date: DateTime.UtcNow);

        var burn = await user.Client.GetFromJsonAsync<BurnRateResponse>("/api/v1/insights/burn-rate");

        Assert.Equal(600m, burn!.MonthlyBurnRate);
        Assert.Equal(ThisMonth.AddMonths(-3).ToString("yyyy-MM"), burn.WindowStart);
        Assert.Equal(ThisMonth.AddMonths(-1).ToString("yyyy-MM"), burn.WindowEnd);
        Assert.Equal(100m, burn.SpentSoFar);

        var days = DateTime.DaysInMonth(ThisMonth.Year, ThisMonth.Month);
        var expectedProjection = Math.Round(100m / DateTime.UtcNow.Day * days, 2);
        Assert.Equal(expectedProjection, burn.ProjectedSpending);
        Assert.Equal(Math.Round(expectedProjection / 600m, 4), burn.PaceVsBurnRate);
    }

    [Fact]
    public async Task BurnRate_ForAPastMonth_IncludesThatMonthAndProjectsWhatWasSpent()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var lastMonth = ThisMonth.AddMonths(-1);
        await user.AddTransactionAsync(account, 450m, date: lastMonth.AddDays(1));

        var burn = await user.Client.GetFromJsonAsync<BurnRateResponse>(
            $"/api/v1/insights/burn-rate?month={lastMonth:yyyy-MM}");

        Assert.Equal(lastMonth.ToString("yyyy-MM"), burn!.WindowEnd);
        Assert.Equal(150m, burn.MonthlyBurnRate);
        Assert.Equal(450m, burn.ProjectedSpending);
        Assert.Equal(burn.DaysInMonth, burn.DaysElapsed);
        Assert.Equal(3m, burn.PaceVsBurnRate);
    }

    [Fact]
    public async Task BurnRate_WithNoHistory_IsNull()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 80m);

        var burn = await user.Client.GetFromJsonAsync<BurnRateResponse>("/api/v1/insights/burn-rate");

        Assert.Null(burn!.MonthlyBurnRate);
        Assert.Null(burn.PaceVsBurnRate);
        Assert.Equal(80m, burn.SpentSoFar);
    }

    [Fact]
    public async Task BurnRate_OnlyUsesTheCallersData()
    {
        var alice = await TestUser.CreateAsync(factory);
        var account = await alice.AddAccountAsync();
        await alice.AddTransactionAsync(account, 300m, date: ThisMonth.AddMonths(-1).AddDays(1));
        var bob = await TestUser.CreateAsync(factory);

        var burn = await bob.Client.GetFromJsonAsync<BurnRateResponse>("/api/v1/insights/burn-rate");

        Assert.Null(burn!.MonthlyBurnRate);
    }
}
