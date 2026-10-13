using Microsoft.EntityFrameworkCore;
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
        var first = month.AddMonths(-(Months - 1));

        var totals = await db.Transactions
            .CountedInCashflow()
            .Where(t => t.Date >= first.Start && t.Date < month.End)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Income = g.Where(t => t.Amount < 0).Sum(t => -t.Amount),
                Expenses = g.Where(t => t.Amount > 0).Sum(t => t.Amount)
            })
            .ToListAsync(cancellationToken);

        var byMonth = totals.ToDictionary(t => new InsightMonth(t.Year, t.Month));

        return new CashflowTrendResponse
        {
            Months = Enumerable.Range(0, Months)
                .Select(i => first.AddMonths(i))
                .Select(m =>
                {
                    byMonth.TryGetValue(m, out var t);
                    var income = t?.Income ?? 0;
                    var expenses = t?.Expenses ?? 0;
                    return new CashflowMonth
                    {
                        Month = m.ToString(),
                        Income = income,
                        Expenses = expenses,
                        Net = income - expenses
                    };
                })
                .ToList()
        };
    }
}
