using System.Net.Http.Json;
using PersonalFinance.Api.Services.Insights;
using PersonalFinance.Shared.DTOs.Insights;
using Charge = PersonalFinance.Api.Services.Insights.RecurringDetector.Charge;

namespace PersonalFinance.Tests;

// Recurring charge detection (#125): the algorithm on its own, then the
// insight end to end.
public class RecurringDetectorTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 16);

    private static IEnumerable<Charge> Every(int days, int count, decimal amount, string description = "NETFLIX.COM",
        DateOnly? last = null)
    {
        var end = last ?? AsOf.AddDays(-2);
        for (var i = count - 1; i >= 0; i--)
            yield return new Charge($"{description} {1000 + i}", amount, end.AddDays(-days * i), "ENTERTAINMENT");
    }

    private static IEnumerable<Charge> Monthly(int count, decimal amount, string description = "SPOTIFY", DateOnly? last = null)
    {
        var end = last ?? new DateOnly(2026, 11, 3);
        for (var i = count - 1; i >= 0; i--)
            yield return new Charge(description, amount, end.AddMonths(-i), "ENTERTAINMENT");
    }

    [Theory]
    [InlineData(7, "weekly")]
    [InlineData(14, "biweekly")]
    [InlineData(91, "quarterly")]
    public void Detects_FixedDayCadences(int days, string cadence)
    {
        var series = Assert.Single(RecurringDetector.Detect(Every(days, 4, 12m), AsOf));

        Assert.Equal(cadence, series.Cadence);
        Assert.Equal(12m, series.TypicalAmount);
        Assert.Equal(4, series.Occurrences);
        // Quarterly bills follow the calendar (same day, three months on),
        // the others a fixed number of days.
        var last = AsOf.AddDays(-2);
        Assert.Equal(cadence == "quarterly" ? last.AddMonths(3) : last.AddDays(days), series.NextDate);
    }

    [Fact]
    public void Detects_Monthly_AcrossMonthsOfDifferentLengths_AndNormalisesItsCost()
    {
        var series = Assert.Single(RecurringDetector.Detect(Monthly(6, 10.99m), AsOf));

        Assert.Equal("monthly", series.Cadence);
        Assert.Equal(new DateOnly(2026, 12, 3), series.NextDate);
        Assert.Equal(10.99m, series.MonthlyCost);
    }

    [Fact]
    public void Detects_Yearly_FromTwoCharges_AtATwelfthOfTheCostPerMonth()
    {
        var charges = new[]
        {
            new Charge("AMAZON PRIME ANNUAL", 120m, new DateOnly(2024, 12, 1), "GENERAL_MERCHANDISE"),
            new Charge("AMAZON PRIME ANNUAL", 120m, new DateOnly(2025, 12, 1), "GENERAL_MERCHANDISE")
        };

        var series = Assert.Single(RecurringDetector.Detect(charges, new DateOnly(2026, 6, 1)));

        Assert.Equal("yearly", series.Cadence);
        Assert.Equal(10m, series.MonthlyCost);
        Assert.Equal(new DateOnly(2026, 12, 1), series.NextDate);
    }

    [Fact]
    public void GroupsAMerchantWhoseDescriptionCarriesChangingReferenceNumbers()
    {
        // Every() appends a different number to each description.
        var series = Assert.Single(RecurringDetector.Detect(Every(30, 4, 15.49m, "Netflix.com"), AsOf));
        Assert.Equal(4, series.Occurrences);
        Assert.Equal("netflix com", RecurringDetector.Normalise("NETFLIX.COM 8473"));
    }

    [Fact]
    public void ToleratesABillThatVaries_AndOneLateMonth()
    {
        var charges = new[]
        {
            new Charge("CITY ELECTRIC", 82m, new DateOnly(2026, 6, 10), "RENT_AND_UTILITIES"),
            new Charge("CITY ELECTRIC", 95m, new DateOnly(2026, 7, 10), "RENT_AND_UTILITIES"),
            new Charge("CITY ELECTRIC", 88m, new DateOnly(2026, 8, 14), "RENT_AND_UTILITIES"),
            new Charge("CITY ELECTRIC", 79m, new DateOnly(2026, 9, 10), "RENT_AND_UTILITIES"),
            new Charge("CITY ELECTRIC", 101m, new DateOnly(2026, 10, 10), "RENT_AND_UTILITIES")
        };

        var series = Assert.Single(RecurringDetector.Detect(charges, AsOf));
        Assert.Equal("monthly", series.Cadence);
        Assert.Equal(88m, series.TypicalAmount);
    }

    [Fact]
    public void IgnoresIrregularShoppingAtOneStore()
    {
        var charges = new[] { 2, 9, 40, 47, 90, 101, 150 }
            .Select((d, i) => new Charge("TESCO STORES", 20m + i * 13, AsOf.AddDays(-d), "FOOD_AND_DRINK"));

        Assert.Empty(RecurringDetector.Detect(charges, AsOf));
    }

    [Fact]
    public void IgnoresAMerchantWhoseAmountsJumpAround()
    {
        var charges = Monthly(5, 0).Select((c, i) => c with { Amount = new[] { 9m, 60m, 14m, 120m, 35m }[i] });
        Assert.Empty(RecurringDetector.Detect(charges, AsOf));
    }

    [Fact]
    public void NeedsThreeChargesForAnythingButYearly() =>
        Assert.Empty(RecurringDetector.Detect(Monthly(2, 10m), AsOf));

    [Fact]
    public void DropsASubscriptionThatStopped()
    {
        // Monthly until July, nothing since: cancelled.
        Assert.Empty(RecurringDetector.Detect(Monthly(6, 10m, last: new DateOnly(2026, 7, 3)), AsOf));
    }

    [Fact]
    public void IgnoresMoneyIn() =>
        Assert.Empty(RecurringDetector.Detect(Monthly(6, -2500m, "ACME PAYROLL"), AsOf));

    [Fact]
    public void ListsTheSoonestNextChargeFirst()
    {
        var charges = Monthly(4, 10m, "SPOTIFY", last: new DateOnly(2026, 11, 3))
            .Concat(Monthly(4, 50m, "GYM", last: new DateOnly(2026, 10, 25)));

        var series = RecurringDetector.Detect(charges, AsOf);

        Assert.Equal(["GYM", "SPOTIFY"], series.Select(s => s.Merchant));
    }
}

public class RecurringInsightTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Insight_FindsTheUsersSubscriptions_AndLeavesOutTransfersAndPending()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var today = DateTime.UtcNow.Date;
        for (var i = 0; i < 4; i++)
        {
            var date = DateTime.SpecifyKind(today.AddMonths(-i).AddDays(-1), DateTimeKind.Utc);
            await user.AddTransactionAsync(account, 15.99m, "ENTERTAINMENT", date: date, description: "NETFLIX.COM");
            // Same shape, but a transfer between own accounts: not a subscription.
            await user.AddTransactionAsync(account, 500m, "TRANSFER_OUT", date: date, description: "TO SAVINGS");
        }
        await user.AddTransactionAsync(account, 15.99m, "ENTERTAINMENT", date: DateTime.UtcNow, pending: true, description: "NETFLIX.COM");

        var response = await user.Client.GetFromJsonAsync<RecurringResponse>("/api/v1/insights/recurring");

        var netflix = Assert.Single(response!.Series);
        Assert.Equal("NETFLIX.COM", netflix.Merchant);
        Assert.Equal("monthly", netflix.Cadence);
        Assert.Equal(4, netflix.Occurrences);
        Assert.Equal(15.99m, response.TotalMonthlyCost);
    }

    [Fact]
    public async Task Insight_IsPerUser()
    {
        var alice = await TestUser.CreateAsync(factory);
        var bob = await TestUser.CreateAsync(factory);
        var account = await alice.AddAccountAsync();
        for (var i = 0; i < 4; i++)
            await alice.AddTransactionAsync(account, 9.99m, date: DateTime.SpecifyKind(DateTime.UtcNow.Date.AddMonths(-i).AddDays(-1), DateTimeKind.Utc), description: "SPOTIFY");

        var bobs = await bob.Client.GetFromJsonAsync<RecurringResponse>("/api/v1/insights/recurring");

        Assert.Empty(bobs!.Series);
    }
}
