using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// Credit's share of spending for each of the 12 months ending with the
// requested one, oldest first - whether the card is being leaned on more.
public class CreditShareTrendInsight(AppDbContext db) : IInsight<CreditShareTrendResponse>
{
    public static string Key => "credit-share-trend";

    public const int Months = 12;

    public async Task<CreditShareTrendResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var first = month.AddMonths(-(Months - 1));
        var rows = await PaymentMethod.SpendingAsync(db, first.Start, month.End, cancellationToken);
        var byMonth = rows.ToLookup(r => new InsightMonth(r.Date.Year, r.Date.Month));

        return new CreditShareTrendResponse
        {
            Months = Enumerable.Range(0, Months)
                .Select(i => first.AddMonths(i))
                .Select(m =>
                {
                    var credit = byMonth[m].Where(r => r.AccountType == PaymentMethod.Credit).Sum(r => r.Amount);
                    var debit = byMonth[m].Where(r => r.AccountType == PaymentMethod.Debit).Sum(r => r.Amount);
                    return new CreditShareMonth
                    {
                        Month = m.ToString(),
                        Credit = credit,
                        Debit = debit,
                        CreditShare = Ratio.Of(credit, credit + debit)
                    };
                })
                .ToList()
        };
    }
}
