using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// How the month's spending divides between credit cards and debit, with
// the average purchase on each.
public class CreditDebitSplitInsight(AppDbContext db) : IInsight<CreditDebitSplitResponse>
{
    public static string Key => "credit-debit-split";

    public async Task<CreditDebitSplitResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var rows = await PaymentMethod.SpendingAsync(db, month.Start, month.End, cancellationToken);
        var credit = rows.Where(r => r.AccountType == PaymentMethod.Credit).ToList();
        var debit = rows.Where(r => r.AccountType == PaymentMethod.Debit).ToList();
        var creditTotal = credit.Sum(r => r.Amount);
        var debitTotal = debit.Sum(r => r.Amount);
        var total = creditTotal + debitTotal;

        return new CreditDebitSplitResponse
        {
            Month = month.ToString(),
            Credit = new MethodSpend
            {
                Amount = creditTotal,
                Share = Ratio.Of(creditTotal, total),
                Purchases = credit.Count,
                AveragePurchase = credit.Count == 0 ? null : Math.Round(creditTotal / credit.Count, 2)
            },
            Debit = new MethodSpend
            {
                Amount = debitTotal,
                Share = Ratio.Of(debitTotal, total),
                Purchases = debit.Count,
                AveragePurchase = debit.Count == 0 ? null : Math.Round(debitTotal / debit.Count, 2)
            }
        };
    }
}
