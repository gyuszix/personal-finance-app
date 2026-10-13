using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// DTI (take-home) = monthly debt payments ÷ monthly take-home income, both
// averaged over the BurnRate window so one odd month doesn't swing it.
//
// - Income: money in categorised INCOME - take-home pay, since that's what
//   lands in the bank. Lenders use gross pay, so this reads higher than a
//   lender's DTI and their thresholds don't apply.
// - Debt payments: LOAN_PAYMENTS money out of bank or card accounts, except
//   credit card payments - paying a card in full is clearing spending
//   already counted, not servicing debt, and would overstate DTI. The
//   receiving loan account's side of a payment isn't counted again.
public class DebtToIncomeInsight(AppDbContext db, TimeProvider clock) : IInsight<DebtToIncomeResponse>
{
    public static string Key => "debt-to-income";

    public async Task<DebtToIncomeResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var (first, last) = BurnRate.Window(month, clock.GetUtcNow());

        var rows = await (
            from t in db.Transactions
            join a in db.Accounts on t.AccountId equals a.AccountId
            where !t.IsPending && t.Date >= first.Start && t.Date < last.End
                  && (t.CategoryPrimary == "INCOME" || t.CategoryPrimary == "LOAN_PAYMENTS")
            select new { a.AccountType, t.CategoryPrimary, t.CategoryDetailed, t.Amount })
            .ToListAsync(cancellationToken);

        var income = rows
            .Where(r => r.CategoryPrimary == "INCOME" && r.Amount < 0)
            .Sum(r => -r.Amount);

        var payments = rows
            .Where(r => r.CategoryPrimary == "LOAN_PAYMENTS"
                        && r.CategoryDetailed != Spending.CreditCardPayment
                        && r.Amount > 0
                        && r.AccountType is PaymentMethod.Debit or PaymentMethod.Credit)
            .ToList();

        var monthlyIncome = Math.Round(income / BurnRate.WindowMonths, 2);
        var monthlyPayments = Math.Round(payments.Sum(p => p.Amount) / BurnRate.WindowMonths, 2);

        return new DebtToIncomeResponse
        {
            Month = month.ToString(),
            WindowStart = first.ToString(),
            WindowEnd = last.ToString(),
            MonthlyIncome = monthlyIncome,
            MonthlyDebtPayments = monthlyPayments,
            DebtToIncome = Ratio.Of(monthlyPayments, monthlyIncome),
            Payments = payments
                .GroupBy(p => p.CategoryDetailed ?? "LOAN_PAYMENTS")
                .Select(g => new DebtPaymentType
                {
                    Type = g.Key,
                    MonthlyAmount = Math.Round(g.Sum(p => p.Amount) / BurnRate.WindowMonths, 2)
                })
                .OrderByDescending(p => p.MonthlyAmount)
                .ToList()
        };
    }
}
