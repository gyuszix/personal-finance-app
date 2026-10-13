using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// Spent vs limit per category for the month. The limit is the saved one,
// or the suggestion (see Budgets) until the user saves one. Over-budget
// categories first, most dollars over first; then by share of the limit used.
public class BudgetVarianceInsight(AppDbContext db, TimeProvider clock) : IInsight<BudgetVarianceResponse>
{
    public static string Key => "budget-variance";

    public async Task<BudgetVarianceResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var lines = await Budgets.ForAsync(db, month, clock.GetUtcNow(), cancellationToken);

        var spent = await db.Transactions
            .CountedInCashflow()
            .Where(t => t.Amount > 0 && t.Date >= month.Start && t.Date < month.End && t.CategoryPrimary != null)
            .GroupBy(t => t.CategoryPrimary!)
            .Select(g => new { Category = g.Key, Total = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(g => g.Category, g => g.Total, cancellationToken);

        // A category spent in this month but never before still gets a row,
        // with a zero suggestion, so new spending doesn't hide.
        var categories = lines.ToDictionary(l => l.Category);
        foreach (var category in spent.Keys.Where(Budgets.Categories.Contains).Where(c => !categories.ContainsKey(c)))
            categories[category] = new Budgets.Line(category, null, 0);

        var rows = categories.Values
            .Select(l =>
            {
                var amount = spent.GetValueOrDefault(l.Category);
                return new BudgetVarianceLine
                {
                    Category = l.Category,
                    Spent = amount,
                    Limit = l.Limit,
                    IsSuggested = l.IsSuggested,
                    Remaining = l.Limit - amount,
                    Used = Ratio.Of(amount, l.Limit)
                };
            })
            .OrderByDescending(r => r.Remaining < 0)
            .ThenBy(r => r.Remaining < 0 ? r.Remaining : 0)
            .ThenByDescending(r => r.Used ?? 0)
            .ThenBy(r => r.Category)
            .ToList();

        var totalLimit = rows.Sum(r => r.Limit);
        var totalSpent = rows.Sum(r => r.Spent);
        return new BudgetVarianceResponse
        {
            Month = month.ToString(),
            TotalLimit = totalLimit,
            TotalSpent = totalSpent,
            TotalUsed = Ratio.Of(totalSpent, totalLimit),
            OverBudget = rows.Count(r => r.Remaining < 0),
            Categories = rows
        };
    }
}
