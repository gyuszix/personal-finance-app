using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Services.Insights;

// Average monthly spending over the three most recent complete months up
// to the given one. The current month is still running, so it's never one
// of them: asked for this month, the window is the three before it.
public static class BurnRate
{
    public const int WindowMonths = 3;

    // The three complete months ending with the given one - or, for the
    // current month, with the one before it. Shared by every metric that
    // averages a "usual month".
    public static (InsightMonth First, InsightMonth Last) Window(InsightMonth month, DateTimeOffset now)
    {
        var last = month == InsightMonth.Containing(now) ? month.AddMonths(-1) : month;
        return (last.AddMonths(-(WindowMonths - 1)), last);
    }

    public record Result(decimal? MonthlyAverage, InsightMonth First, InsightMonth Last);

    public static async Task<Result> ForAsync(
        AppDbContext db, InsightMonth month, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (first, last) = Window(month, now);
        var months = await MonthlyCashflow.ForAsync(db, first, last, cancellationToken);
        var total = months.Sum(m => m.Spending);

        // No spending at all in the window means no history to average, not
        // a burn rate of zero.
        return new Result(total == 0 ? null : Math.Round(total / WindowMonths, 2), first, last);
    }
}
