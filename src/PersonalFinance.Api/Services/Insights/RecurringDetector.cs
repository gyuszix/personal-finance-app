using System.Text.RegularExpressions;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// Finds recurring charges in a user's spending (#125). Deliberately simple
// and explainable - every series it reports can be checked by eye:
//
// 1. Group charges by merchant, normalised so "NETFLIX.COM 8473" and
//    "Netflix.com 1192" are one merchant (case, punctuation and any token
//    containing a digit are dropped).
// 2. Classify the cadence from the median gap between charges.
// 3. Keep it only if it's regular: most gaps (75%) fit the cadence and most
//    amounts (75%) are within 25% (or $5) of the median - a monthly bill can
//    wobble, random shopping at one store doesn't qualify.
// 4. Drop series that have stopped: the last charge is more than one and a
//    half cycles ago (a cancelled subscription).
// 5. Enough history: 3 charges, or 2 for yearly.
public static partial class RecurringDetector
{
    public record Charge(string Description, decimal Amount, DateOnly Date, string? Category);

    private record Cadence(string Name, double Days, double MinGap, double MaxGap, int MinOccurrences, Func<DateOnly, DateOnly> Next);

    private static readonly Cadence[] Cadences =
    [
        new("weekly", 7, 5, 9, 3, d => d.AddDays(7)),
        new("biweekly", 14, 11, 17, 3, d => d.AddDays(14)),
        new("monthly", 365.25 / 12, 26, 35, 3, d => d.AddMonths(1)),
        new("quarterly", 365.25 / 4, 80, 100, 3, d => d.AddMonths(3)),
        new("yearly", 365.25, 350, 380, 2, d => d.AddYears(1))
    ];

    private const double RegularShare = 0.75;
    private const decimal AmountTolerance = 0.25m;
    private const decimal AmountToleranceFloor = 5m;

    public static List<RecurringSeries> Detect(IEnumerable<Charge> charges, DateOnly asOf)
    {
        var series = new List<RecurringSeries>();

        foreach (var group in charges.Where(c => c.Amount > 0 && c.Date <= asOf).GroupBy(c => Normalise(c.Description)))
        {
            if (group.Key.Length < 2) continue;

            // One charge per day: a split payment or a retry isn't a second cycle.
            var byDay = group.GroupBy(c => c.Date).Select(d => d.OrderByDescending(c => c.Amount).First()).OrderBy(c => c.Date).ToList();
            if (byDay.Count < 2) continue;

            var gaps = byDay.Zip(byDay.Skip(1), (a, b) => (double)(b.Date.DayNumber - a.Date.DayNumber)).ToList();
            var medianGap = Median(gaps);
            var cadence = Cadences.FirstOrDefault(c => medianGap >= c.MinGap && medianGap <= c.MaxGap);
            if (cadence is null || byDay.Count < cadence.MinOccurrences) continue;

            if (gaps.Count(g => g >= cadence.MinGap && g <= cadence.MaxGap) < RegularShare * gaps.Count) continue;

            var typical = Median(byDay.Select(c => c.Amount).ToList());
            var tolerance = Math.Max(typical * AmountTolerance, AmountToleranceFloor);
            if (byDay.Count(c => Math.Abs(c.Amount - typical) <= tolerance) < RegularShare * byDay.Count) continue;

            var last = byDay[^1];
            if (asOf.DayNumber - last.Date.DayNumber > cadence.Days * 1.5) continue;

            var next = cadence.Next(last.Date);
            while (next < asOf) next = cadence.Next(next); // missed one but not lapsed: the next one ahead

            series.Add(new RecurringSeries
            {
                Merchant = last.Description.Trim(),
                Category = last.Category,
                Cadence = cadence.Name,
                TypicalAmount = Math.Round(typical, 2),
                MonthlyCost = Math.Round(typical * (decimal)(365.25 / 12 / cadence.Days), 2),
                Occurrences = byDay.Count,
                LastDate = last.Date,
                NextDate = next
            });
        }

        return series.OrderBy(s => s.NextDate).ThenByDescending(s => s.MonthlyCost).ToList();
    }

    public static string Normalise(string description)
    {
        var words = NonWord().Split(description.ToLowerInvariant())
            .Where(w => w.Length > 0 && !w.Any(char.IsDigit));
        return string.Join(' ', words);
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static decimal Median(List<decimal> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();
}
