using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Services.Insights;

public record MonthTotals(InsightMonth Month, decimal Income, decimal Spending)
{
    public decimal Net => Income - Spending;
}

// Income and spending per month, by the shared Spending definition.
public static class MonthlyCashflow
{
    // Every month from first to last inclusive, oldest first; months with no
    // transactions are zeros.
    public static async Task<List<MonthTotals>> ForAsync(
        AppDbContext db, InsightMonth first, InsightMonth last, CancellationToken cancellationToken)
    {
        var totals = await db.Transactions
            .CountedInCashflow()
            .Where(t => t.Date >= first.Start && t.Date < last.End)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                // Plaid: negative Amount = money in, positive = money out.
                Income = g.Where(t => t.Amount < 0).Sum(t => -t.Amount),
                Spending = g.Where(t => t.Amount > 0).Sum(t => t.Amount)
            })
            .ToListAsync(cancellationToken);

        var byMonth = totals.ToDictionary(t => new InsightMonth(t.Year, t.Month));
        var months = new List<MonthTotals>();
        for (var m = first; m.Start <= last.Start; m = m.AddMonths(1))
        {
            byMonth.TryGetValue(m, out var t);
            months.Add(new MonthTotals(m, t?.Income ?? 0, t?.Spending ?? 0));
        }
        return months;
    }
}
