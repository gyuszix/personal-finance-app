using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// The month's spending per Plaid primary category, largest first. Every
// category is returned; grouping the tail into "Other" is the card's call.
public class SpendingByCategoryInsight(AppDbContext db) : IInsight<SpendingByCategoryResponse>
{
    public static string Key => "spending-by-category";

    public async Task<SpendingByCategoryResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        // Plaid: positive Amount = money out.
        var totals = await db.Transactions
            .CountedInCashflow()
            .Where(t => t.Amount > 0 && t.Date >= month.Start && t.Date < month.End)
            .GroupBy(t => t.CategoryPrimary ?? "Uncategorized")
            .Select(g => new { Category = g.Key, Amount = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        var total = totals.Sum(t => t.Amount);

        return new SpendingByCategoryResponse
        {
            Month = month.ToString(),
            Total = total,
            Categories = totals
                .OrderByDescending(t => t.Amount)
                .Select(t => new CategorySpend
                {
                    Category = t.Category,
                    Amount = t.Amount,
                    Share = Ratio.Of(t.Amount, total)
                })
                .ToList()
        };
    }
}
