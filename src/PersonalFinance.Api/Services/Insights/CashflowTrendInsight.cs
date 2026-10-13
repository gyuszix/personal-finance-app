using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// Income, spending and net for the 12 months ending with the requested one,
// oldest first. Months with no transactions are present as zeros, so the
// chart's axis doesn't skip.
public class CashflowTrendInsight(AppDbContext db) : IInsight<CashflowTrendResponse>
{
    public static string Key => "cashflow-trend";

    public const int Months = 12;

    public async Task<CashflowTrendResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var months = await MonthlyCashflow.ForAsync(db, month.AddMonths(-(Months - 1)), month, cancellationToken);

        return new CashflowTrendResponse
        {
            Months = months
                .Select(m => new CashflowMonth
                {
                    Month = m.Month.ToString(),
                    Income = m.Income,
                    Expenses = m.Spending,
                    Net = m.Net
                })
                .ToList()
        };
    }
}
