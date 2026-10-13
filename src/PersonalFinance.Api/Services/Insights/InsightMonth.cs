using System.Globalization;

namespace PersonalFinance.Api.Services.Insights;

// The calendar month an insight is calculated for, in UTC.
public readonly record struct InsightMonth(int Year, int Month)
{
    // Nothing older is meaningful bank data, and it bounds how far back a
    // trend query can be pushed.
    public static readonly InsightMonth Earliest = new(2000, 1);

    public DateTime Start => new(Year, Month, 1, 0, 0, 0, DateTimeKind.Utc);
    public DateTime End => Start.AddMonths(1);
    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    public InsightMonth AddMonths(int months)
    {
        var start = Start.AddMonths(months);
        return new InsightMonth(start.Year, start.Month);
    }

    public static InsightMonth Containing(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        return new InsightMonth(utc.Year, utc.Month);
    }

    public override string ToString() => $"{Year:D4}-{Month:D2}";

    // Null or empty means the current month. Otherwise yyyy-MM, from
    // Earliest up to the current month - there's no data in the future.
    public static bool TryParse(string? value, DateTimeOffset now, out InsightMonth month, out string? error)
    {
        var current = Containing(now);
        month = current;
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (!DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            error = "month must be in yyyy-MM format, e.g. 2026-09";
            return false;
        }

        month = new InsightMonth(parsed.Year, parsed.Month);
        if (month.Start < Earliest.Start || month.Start > current.Start)
        {
            error = $"month must be between {Earliest} and {current}";
            return false;
        }

        return true;
    }
}
