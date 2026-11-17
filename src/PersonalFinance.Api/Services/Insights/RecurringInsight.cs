using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// Recurring charges as of the selected month (#125): today for the current
// month, the month's last day for a past one. Looks back 25 months, enough
// for a yearly charge to show up twice. Only real spending counts
// (Spending.CountedInCashflow: posted, no transfers or card payments), with
// each transaction's effective category (rules included, #124).
public class RecurringInsight(AppDbContext db, TimeProvider clock) : IInsight<RecurringResponse>
{
    public static string Key => "recurring";

    public async Task<RecurringResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var asOf = month == InsightMonth.Containing(now)
            ? DateOnly.FromDateTime(now.UtcDateTime)
            : DateOnly.FromDateTime(month.End.AddDays(-1));
        var from = asOf.AddMonths(-25).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = asOf.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var charges = await db.Transactions
            .CountedInCashflow()
            .Where(t => t.Amount > 0 && t.Date >= from && t.Date < to)
            .Select(t => new { t.Description, t.Amount, t.Date, t.CategoryPrimary })
            .ToListAsync(cancellationToken);

        var series = RecurringDetector.Detect(
            charges.Select(c => new RecurringDetector.Charge(c.Description, c.Amount, DateOnly.FromDateTime(c.Date), c.CategoryPrimary)),
            asOf);

        return new RecurringResponse
        {
            Month = month.ToString(),
            AsOf = asOf,
            Series = series,
            TotalMonthlyCost = series.Sum(s => s.MonthlyCost)
        };
    }
}
