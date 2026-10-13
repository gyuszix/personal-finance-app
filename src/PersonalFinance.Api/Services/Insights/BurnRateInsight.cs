using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// The burn rate (see BurnRate), and how this month is pacing against it:
// spending so far ÷ days elapsed × days in the month. A past month is
// complete, so its "projection" is just what was spent.
public class BurnRateInsight(AppDbContext db, TimeProvider clock) : IInsight<BurnRateResponse>
{
    public static string Key => "burn-rate";

    public async Task<BurnRateResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var burn = await BurnRate.ForAsync(db, month, now, cancellationToken);
        var spent = (await MonthlyCashflow.ForAsync(db, month, month, cancellationToken))[0].Spending;

        var isCurrent = month == InsightMonth.Containing(now);
        var daysElapsed = isCurrent ? now.UtcDateTime.Day : month.DaysInMonth;
        var projected = Math.Round(spent / daysElapsed * month.DaysInMonth, 2);

        return new BurnRateResponse
        {
            Month = month.ToString(),
            MonthlyBurnRate = burn.MonthlyAverage,
            WindowStart = burn.First.ToString(),
            WindowEnd = burn.Last.ToString(),
            SpentSoFar = spent,
            DaysElapsed = daysElapsed,
            DaysInMonth = month.DaysInMonth,
            ProjectedSpending = projected,
            PaceVsBurnRate = burn.MonthlyAverage is { } average ? Ratio.Of(projected, average) : null
        };
    }
}
