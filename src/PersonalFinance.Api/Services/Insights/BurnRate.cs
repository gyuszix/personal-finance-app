using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Services.Insights;

// Average monthly spending over the three most recent complete months up
// to the given one. The current month is still running, so it's never one
// of them: asked for this month, the window is the three before it.
public static class BurnRate
{
    public const int WindowMonths = 3;

    public record Result(decimal? MonthlyAverage, InsightMonth First, InsightMonth Last);

    public static async Task<Result> ForAsync(
        AppDbContext db, InsightMonth month, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var last = month == InsightMonth.Containing(now) ? month.AddMonths(-1) : month;
        var first = last.AddMonths(-(WindowMonths - 1));
        var months = await MonthlyCashflow.ForAsync(db, first, last, cancellationToken);
        var total = months.Sum(m => m.Spending);

        // No spending at all in the window means no history to average, not
        // a burn rate of zero.
        return new Result(total == 0 ? null : Math.Round(total / WindowMonths, 2), first, last);
    }
}
