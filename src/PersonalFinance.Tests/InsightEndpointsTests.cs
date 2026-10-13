using System.Net;
using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Tests;

// The shared /insights plumbing (sign-in, month validation, isolation,
// caching) and the spending-by-category and cash flow trend metrics (#85).
public class InsightEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static readonly DateTime ThisMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string> Insights => ["spending-by-category", "cashflow-trend"];

    [Theory]
    [MemberData(nameof(Insights))]
    public async Task Insight_RequiresSignIn(string insight)
    {
        var response = await factory.CreateClient().GetAsync($"/api/v1/insights/{insight}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("2026/03")]
    [InlineData("March")]
    [InlineData("1999-12")]
    [InlineData("2026-13")]
    public async Task Insight_RejectsMalformedOrOutOfRangeMonth(string month)
    {
        var user = await TestUser.CreateAsync(factory);

        var response = await user.Client.GetAsync($"/api/v1/insights/spending-by-category?month={month}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Insight_RejectsAFutureMonth()
    {
        var user = await TestUser.CreateAsync(factory);
        var nextMonth = ThisMonth.AddMonths(1).ToString("yyyy-MM");

        var response = await user.Client.GetAsync($"/api/v1/insights/spending-by-category?month={nextMonth}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SpendingByCategory_TotalsSpendingLargestFirst()
    {
        var user = await TestUser.CreateAsync(factory);
        var checking = await user.AddAccountAsync();
        var card = await user.AddAccountAsync(accountType: "Credit");
        var today = DateTime.UtcNow;
        await user.AddTransactionAsync(card, 60m, category: "FOOD_AND_DRINK", date: today);
        await user.AddTransactionAsync(checking, 20m, category: "FOOD_AND_DRINK", date: today);
        await user.AddTransactionAsync(checking, 120m, category: "RENT_AND_UTILITIES", date: today);
        // None of these are spending:
        await user.AddTransactionAsync(checking, -900m, category: "INCOME", date: today);
        await user.AddTransactionAsync(checking, 500m, category: "TRANSFER_OUT", date: today);
        await user.AddTransactionAsync(checking, 60m, category: "LOAN_PAYMENTS", date: today,
            detailedCategory: "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT");
        await user.AddTransactionAsync(checking, 40m, category: "FOOD_AND_DRINK", date: today, pending: true);
        await user.AddTransactionAsync(checking, 70m, category: "FOOD_AND_DRINK", date: ThisMonth.AddDays(-1));

        var spending = await user.Client.GetFromJsonAsync<SpendingByCategoryResponse>("/api/v1/insights/spending-by-category");

        Assert.Equal(ThisMonth.ToString("yyyy-MM"), spending!.Month);
        Assert.Equal(200m, spending.Total);
        Assert.Equal(["RENT_AND_UTILITIES", "FOOD_AND_DRINK"], spending.Categories.Select(c => c.Category));
        Assert.Equal([120m, 80m], spending.Categories.Select(c => c.Amount));
        Assert.Equal([0.6m, 0.4m], spending.Categories.Select(c => c.Share));
    }

    [Fact]
    public async Task SpendingByCategory_WithNothingSpent_IsEmptyNotAnError()
    {
        var user = await TestUser.CreateAsync(factory);

        var spending = await user.Client.GetFromJsonAsync<SpendingByCategoryResponse>("/api/v1/insights/spending-by-category");

        Assert.Equal(0m, spending!.Total);
        Assert.Empty(spending.Categories);
    }

    [Fact]
    public async Task CashflowTrend_ReturnsTwelveMonthsOldestFirst_WithZerosForQuietMonths()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var lastMonth = ThisMonth.AddMonths(-1);
        await user.AddTransactionAsync(account, -1000m, category: "INCOME", date: lastMonth.AddDays(2));
        await user.AddTransactionAsync(account, 250m, category: "FOOD_AND_DRINK", date: lastMonth.AddDays(3));
        await user.AddTransactionAsync(account, 300m, category: "TRANSFER_OUT", date: lastMonth.AddDays(3));
        // Older than the 12-month window
        await user.AddTransactionAsync(account, 999m, category: "FOOD_AND_DRINK", date: lastMonth.AddMonths(-12));

        var trend = await user.Client.GetFromJsonAsync<CashflowTrendResponse>(
            $"/api/v1/insights/cashflow-trend?month={lastMonth:yyyy-MM}");

        Assert.Equal(12, trend!.Months.Count);
        Assert.Equal(lastMonth.AddMonths(-11).ToString("yyyy-MM"), trend.Months[0].Month);
        var last = trend.Months[^1];
        Assert.Equal(lastMonth.ToString("yyyy-MM"), last.Month);
        Assert.Equal(1000m, last.Income);
        Assert.Equal(250m, last.Expenses);
        Assert.Equal(750m, last.Net);
        Assert.All(trend.Months[..^1], m => Assert.Equal(0m, m.Expenses));
    }

    [Fact]
    public async Task Insights_OnlyReturnTheCallersData()
    {
        var alice = await TestUser.CreateAsync(factory);
        var bob = await TestUser.CreateAsync(factory);
        var account = await alice.AddAccountAsync();
        await alice.AddTransactionAsync(account, 50m);
        await alice.AddTransactionAsync(account, -500m, category: "INCOME");

        var spending = await bob.Client.GetFromJsonAsync<SpendingByCategoryResponse>("/api/v1/insights/spending-by-category");
        var trend = await bob.Client.GetFromJsonAsync<CashflowTrendResponse>("/api/v1/insights/cashflow-trend");

        Assert.Empty(spending!.Categories);
        Assert.All(trend!.Months, m => Assert.Equal(0m, m.Income + m.Expenses));
    }

    [Fact]
    public async Task Insights_AreRecalculatedAfterATransactionIsDeleted()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var transaction = await user.AddTransactionAsync(account, 50m);
        await user.Client.GetFromJsonAsync<SpendingByCategoryResponse>("/api/v1/insights/spending-by-category");

        (await user.Client.DeleteAsync($"/api/v1/transactions/{transaction.TransactionId}")).EnsureSuccessStatusCode();

        var spending = await user.Client.GetFromJsonAsync<SpendingByCategoryResponse>("/api/v1/insights/spending-by-category");
        Assert.Equal(0m, spending!.Total);
    }
}
