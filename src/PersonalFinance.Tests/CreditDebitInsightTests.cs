using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Tests;

// Credit vs debit spending (#87).
public class CreditDebitInsightTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string CardPayment = "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT";
    private static readonly DateTime ThisMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private async Task<TestUser> UserWithSpendingAsync()
    {
        var user = await TestUser.CreateAsync(factory);
        var checking = await user.AddAccountAsync(accountType: "Depository");
        var card = await user.AddAccountAsync(accountType: "Credit");
        var loan = await user.AddAccountAsync(accountType: "Loan");
        var today = DateTime.UtcNow;

        await user.AddTransactionAsync(card, 60m, category: "FOOD_AND_DRINK", date: today);
        await user.AddTransactionAsync(card, 90m, category: "FOOD_AND_DRINK", date: today);
        await user.AddTransactionAsync(card, 150m, category: "TRAVEL", date: today);
        await user.AddTransactionAsync(checking, 100m, category: "FOOD_AND_DRINK", date: today);
        await user.AddTransactionAsync(checking, 500m, category: "RENT_AND_UTILITIES", date: today);
        // Not spending, or not from a card or bank account:
        await user.AddTransactionAsync(checking, 240m, category: "LOAN_PAYMENTS", date: today, detailedCategory: CardPayment);
        await user.AddTransactionAsync(card, -240m, category: "LOAN_PAYMENTS", date: today, detailedCategory: CardPayment);
        await user.AddTransactionAsync(checking, 75m, category: "TRANSFER_OUT", date: today);
        await user.AddTransactionAsync(card, 40m, category: "FOOD_AND_DRINK", date: today, pending: true);
        await user.AddTransactionAsync(loan, 30m, category: "LOAN_PAYMENTS", date: today);
        return user;
    }

    [Fact]
    public async Task Split_DividesSpendingByAccountType_WithAveragePurchase()
    {
        var user = await UserWithSpendingAsync();

        var split = await user.Client.GetFromJsonAsync<CreditDebitSplitResponse>("/api/v1/insights/credit-debit-split");

        Assert.Equal(300m, split!.Credit.Amount);
        Assert.Equal(3, split.Credit.Purchases);
        Assert.Equal(100m, split.Credit.AveragePurchase);
        Assert.Equal(600m, split.Debit.Amount);
        Assert.Equal(300m, split.Debit.AveragePurchase);
        Assert.Equal(0.3333m, split.Credit.Share);
        Assert.Equal(0.6667m, split.Debit.Share);
    }

    [Fact]
    public async Task Split_WithNoSpending_HasNullRatiosNotZeroes()
    {
        var user = await TestUser.CreateAsync(factory);

        var split = await user.Client.GetFromJsonAsync<CreditDebitSplitResponse>("/api/v1/insights/credit-debit-split");

        Assert.Null(split!.Credit.Share);
        Assert.Null(split.Credit.AveragePurchase);
        Assert.Null(split.Debit.Share);
    }

    [Fact]
    public async Task ByCategory_SplitsEachCategoryLargestFirst()
    {
        var user = await UserWithSpendingAsync();

        var byCategory = await user.Client.GetFromJsonAsync<CreditDebitByCategoryResponse>("/api/v1/insights/credit-debit-by-category");

        Assert.Equal(["RENT_AND_UTILITIES", "FOOD_AND_DRINK", "TRAVEL"], byCategory!.Categories.Select(c => c.Category));
        var food = byCategory.Categories[1];
        Assert.Equal(150m, food.Credit);
        Assert.Equal(100m, food.Debit);
        Assert.Equal(0.6m, food.CreditShare);
    }

    [Fact]
    public async Task CreditShareTrend_HasTwelveMonthsWithNullShareForQuietMonths()
    {
        var user = await UserWithSpendingAsync();

        var trend = await user.Client.GetFromJsonAsync<CreditShareTrendResponse>("/api/v1/insights/credit-share-trend");

        Assert.Equal(12, trend!.Months.Count);
        Assert.Equal(ThisMonth.ToString("yyyy-MM"), trend.Months[^1].Month);
        Assert.Equal(0.3333m, trend.Months[^1].CreditShare);
        Assert.Null(trend.Months[0].CreditShare);
    }

    // The payment appears leaving checking and arriving on the card; it must
    // count once.
    [Fact]
    public async Task Payoff_CountsEachCardPaymentOnce()
    {
        var user = await UserWithSpendingAsync();

        var payoff = await user.Client.GetFromJsonAsync<CardPayoffResponse>("/api/v1/insights/card-payoff");

        Assert.Equal(300m, payoff!.CreditSpending);
        Assert.Equal(240m, payoff.CardPayments);
        Assert.Equal(0.8m, payoff.PayoffRatio);
    }

    [Fact]
    public async Task Payoff_UsesTheCardSide_WhenTheBankSideIsntLinked()
    {
        var user = await TestUser.CreateAsync(factory);
        var card = await user.AddAccountAsync(accountType: "Credit");
        await user.AddTransactionAsync(card, 200m, category: "TRAVEL");
        await user.AddTransactionAsync(card, -250m, category: "LOAN_PAYMENTS", detailedCategory: CardPayment);

        var payoff = await user.Client.GetFromJsonAsync<CardPayoffResponse>("/api/v1/insights/card-payoff");

        Assert.Equal(250m, payoff!.CardPayments);
        Assert.Equal(1.25m, payoff.PayoffRatio);
    }

    [Fact]
    public async Task Payoff_WithNoCreditSpending_IsNull()
    {
        var user = await TestUser.CreateAsync(factory);
        var checking = await user.AddAccountAsync();
        await user.AddTransactionAsync(checking, 100m, category: "LOAN_PAYMENTS", detailedCategory: CardPayment);

        var payoff = await user.Client.GetFromJsonAsync<CardPayoffResponse>("/api/v1/insights/card-payoff");

        Assert.Null(payoff!.PayoffRatio);
    }

    [Fact]
    public async Task CreditDebitInsights_OnlyReturnTheCallersData()
    {
        await UserWithSpendingAsync();
        var bob = await TestUser.CreateAsync(factory);

        var split = await bob.Client.GetFromJsonAsync<CreditDebitSplitResponse>("/api/v1/insights/credit-debit-split");
        var byCategory = await bob.Client.GetFromJsonAsync<CreditDebitByCategoryResponse>("/api/v1/insights/credit-debit-by-category");
        var payoff = await bob.Client.GetFromJsonAsync<CardPayoffResponse>("/api/v1/insights/card-payoff");

        Assert.Equal(0m, split!.Credit.Amount + split.Debit.Amount);
        Assert.Empty(byCategory!.Categories);
        Assert.Equal(0m, payoff!.CardPayments);
    }
}
