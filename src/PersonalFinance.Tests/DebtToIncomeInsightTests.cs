using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Tests;

// Debt-to-income from take-home pay (#89).
public class DebtToIncomeInsightTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static readonly DateTime LastMonth =
        new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);

    [Fact]
    public async Task Dti_IsLoanPaymentsOverTakeHomeIncome_ExcludingCardPayments()
    {
        var user = await TestUser.CreateAsync(factory);
        var checking = await user.AddAccountAsync(accountType: "Depository");
        var loan = await user.AddAccountAsync(accountType: "Loan");
        var day = LastMonth.AddDays(1);
        // Three months' pay and payments, all in last month for simplicity:
        await user.AddTransactionAsync(checking, -9000m, category: "INCOME", date: day);
        await user.AddTransactionAsync(checking, 900m, category: "LOAN_PAYMENTS", date: day, detailedCategory: "LOAN_PAYMENTS_CAR_PAYMENT");
        await user.AddTransactionAsync(checking, 1800m, category: "LOAN_PAYMENTS", date: day, detailedCategory: "LOAN_PAYMENTS_MORTGAGE_PAYMENT");
        // Not debt service:
        await user.AddTransactionAsync(checking, 3000m, category: "LOAN_PAYMENTS", date: day, detailedCategory: "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT");
        await user.AddTransactionAsync(loan, -900m, category: "LOAN_PAYMENTS", date: day, detailedCategory: "LOAN_PAYMENTS_CAR_PAYMENT");
        await user.AddTransactionAsync(checking, -500m, category: "TRANSFER_IN", date: day);
        // This month is outside the window
        await user.AddTransactionAsync(checking, 5000m, category: "LOAN_PAYMENTS", detailedCategory: "LOAN_PAYMENTS_CAR_PAYMENT");

        var dti = await user.Client.GetFromJsonAsync<DebtToIncomeResponse>("/api/v1/insights/debt-to-income");

        Assert.Equal(3000m, dti!.MonthlyIncome);
        Assert.Equal(900m, dti.MonthlyDebtPayments);
        Assert.Equal(0.3m, dti.DebtToIncome);
        Assert.Equal(["LOAN_PAYMENTS_MORTGAGE_PAYMENT", "LOAN_PAYMENTS_CAR_PAYMENT"], dti.Payments.Select(p => p.Type));
        Assert.Equal([600m, 300m], dti.Payments.Select(p => p.MonthlyAmount));
    }

    [Fact]
    public async Task Dti_WithNoIncome_IsNull()
    {
        var user = await TestUser.CreateAsync(factory);
        var checking = await user.AddAccountAsync();
        await user.AddTransactionAsync(checking, 300m, category: "LOAN_PAYMENTS", date: LastMonth.AddDays(1),
            detailedCategory: "LOAN_PAYMENTS_STUDENT_LOAN_PAYMENT");

        var dti = await user.Client.GetFromJsonAsync<DebtToIncomeResponse>("/api/v1/insights/debt-to-income");

        Assert.Null(dti!.DebtToIncome);
        Assert.Equal(100m, dti.MonthlyDebtPayments);
    }

    [Fact]
    public async Task Dti_OnlyUsesTheCallersData()
    {
        var alice = await TestUser.CreateAsync(factory);
        var checking = await alice.AddAccountAsync();
        await alice.AddTransactionAsync(checking, -3000m, category: "INCOME", date: LastMonth.AddDays(1));
        var bob = await TestUser.CreateAsync(factory);

        var dti = await bob.Client.GetFromJsonAsync<DebtToIncomeResponse>("/api/v1/insights/debt-to-income");

        Assert.Equal(0m, dti!.MonthlyIncome);
        Assert.Null(dti.DebtToIncome);
    }
}
