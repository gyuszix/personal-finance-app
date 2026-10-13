using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Services.Insights;

public static class Budgets
{
    // Plaid's personal_finance_category primaries that are spending.
    // INCOME and the transfers aren't, so they can't be budgeted.
    private static readonly string[] CategoryList =
    [
        "BANK_FEES", "ENTERTAINMENT", "FOOD_AND_DRINK", "GENERAL_MERCHANDISE",
        "GENERAL_SERVICES", "GOVERNMENT_AND_NON_PROFIT", "HOME_IMPROVEMENT",
        "LOAN_PAYMENTS", "MEDICAL", "PERSONAL_CARE", "RENT_AND_UTILITIES",
        "TRANSPORTATION", "TRAVEL"
    ];

    public static readonly IReadOnlySet<string> Categories = CategoryList.ToHashSet();

    public record Line(string Category, decimal? SavedLimit, decimal Suggested)
    {
        public decimal Limit => SavedLimit ?? Suggested;
        public bool IsSuggested => SavedLimit == null;
    }

    // Every category with a saved limit or any spending in the burn-rate
    // window. Suggested = average monthly spend over that window, rounded
    // up to the next 10 so it reads like a budget, not a measurement.
    public static async Task<List<Line>> ForAsync(
        AppDbContext db, InsightMonth month, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (first, last) = BurnRate.Window(month, now);

        var spent = await db.Transactions
            .CountedInCashflow()
            .Where(t => t.Amount > 0 && t.Date >= first.Start && t.Date < last.End
                        && t.CategoryPrimary != null && CategoryList.Contains(t.CategoryPrimary))
            .GroupBy(t => t.CategoryPrimary!)
            .Select(g => new { Category = g.Key, Total = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(g => g.Category, g => g.Total, cancellationToken);

        var saved = await db.Budgets
            .ToDictionaryAsync(b => b.Category, b => b.MonthlyLimit, cancellationToken);

        return spent.Keys.Union(saved.Keys)
            .Where(Categories.Contains)
            .Select(c => new Line(
                c,
                saved.TryGetValue(c, out var limit) ? limit : null,
                Suggest(spent.GetValueOrDefault(c))))
            .OrderBy(l => l.Category)
            .ToList();
    }

    private static decimal Suggest(decimal windowTotal) =>
        Math.Ceiling(windowTotal / BurnRate.WindowMonths / 10) * 10;
}
