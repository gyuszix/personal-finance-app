using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Tests;

// Net worth trend from the daily balance snapshots (#93).
public class NetWorthTrendInsightTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Trend_StartsAtTheFirstSnapshot_AndCarriesMissingDaysForward()
    {
        var user = await TestUser.CreateAsync(factory);
        var checking = await user.AddAccountAsync(accountType: "Depository");
        var card = await user.AddAccountAsync(accountType: "Credit");
        await SnapshotAsync(user, checking, Today.AddDays(-3), 1000m);
        await SnapshotAsync(user, card, Today.AddDays(-3), 200m);
        await SnapshotAsync(user, checking, Today.AddDays(-1), 1500m);   // card has no snapshot this day
        await SnapshotAsync(user, card, Today, 300m);

        var trend = await user.Client.GetFromJsonAsync<NetWorthTrendResponse>("/api/v1/insights/net-worth-trend");

        Assert.Equal(Today.ToString("yyyy-MM-dd"), trend!.End);
        Assert.Equal(
            [Today.AddDays(-3), Today.AddDays(-2), Today.AddDays(-1), Today],
            trend.Points.Select(p => DateOnly.Parse(p.Date)));
        Assert.Equal([800m, 800m, 1300m, 1200m], trend.Points.Select(p => p.NetWorth));
        Assert.Equal(300m, trend.Points[^1].Liabilities);
    }

    [Fact]
    public async Task Trend_CarriesInABalanceFromBeforeTheYear()
    {
        var user = await TestUser.CreateAsync(factory);
        var checking = await user.AddAccountAsync(accountType: "Depository");
        await SnapshotAsync(user, checking, Today.AddDays(-400), 700m);

        var trend = await user.Client.GetFromJsonAsync<NetWorthTrendResponse>("/api/v1/insights/net-worth-trend");

        Assert.Equal(365, trend!.Points.Count);
        Assert.All(trend.Points, p => Assert.Equal(700m, p.NetWorth));
    }

    [Fact]
    public async Task Trend_WithNoSnapshots_IsEmpty()
    {
        var user = await TestUser.CreateAsync(factory);
        await user.AddAccountAsync();

        var trend = await user.Client.GetFromJsonAsync<NetWorthTrendResponse>("/api/v1/insights/net-worth-trend");

        Assert.Empty(trend!.Points);
    }

    [Fact]
    public async Task Trend_OnlyUsesTheCallersSnapshots()
    {
        var alice = await TestUser.CreateAsync(factory);
        var account = await alice.AddAccountAsync();
        await SnapshotAsync(alice, account, Today, 1000m);
        var bob = await TestUser.CreateAsync(factory);

        var trend = await bob.Client.GetFromJsonAsync<NetWorthTrendResponse>("/api/v1/insights/net-worth-trend");

        Assert.Empty(trend!.Points);
    }

    private async Task SnapshotAsync(TestUser user, Account account, DateOnly date, decimal balance)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.BalanceSnapshots.Add(new BalanceSnapshot { AccountId = account.AccountId, UserId = user.Id, Date = date, Balance = balance });
        await db.SaveChangesAsync();
    }
}
