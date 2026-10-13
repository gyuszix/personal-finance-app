using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// Per category, how much of the month's spending went on credit vs debit,
// largest category first.
public class CreditDebitByCategoryInsight(AppDbContext db) : IInsight<CreditDebitByCategoryResponse>
{
    public static string Key => "credit-debit-by-category";

    public async Task<CreditDebitByCategoryResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var rows = await PaymentMethod.SpendingAsync(db, month.Start, month.End, cancellationToken);

        return new CreditDebitByCategoryResponse
        {
            Month = month.ToString(),
            Categories = rows
                .GroupBy(r => r.Category)
                .Select(g =>
                {
                    var credit = g.Where(r => r.AccountType == PaymentMethod.Credit).Sum(r => r.Amount);
                    var debit = g.Where(r => r.AccountType == PaymentMethod.Debit).Sum(r => r.Amount);
                    return new CategoryMethodSpend
                    {
                        Category = g.Key,
                        Credit = credit,
                        Debit = debit,
                        CreditShare = Ratio.Of(credit, credit + debit)
                    };
                })
                .OrderByDescending(c => c.Credit + c.Debit)
                .ToList()
        };
    }
}
