using System.Globalization;

namespace PersonalFinance.App.Insights;

// Display formatting shared by the cards. A null ratio (zero income, no
// spending, ...) shows as an em dash, never 0% or infinity.
public static class Format
{
    public const string None = "—";

    public static string Money(decimal value) => value.ToString("C0", CultureInfo.CurrentCulture);

    // 1.2k, 34k, 1.5M - for chart axes.
    public static string CompactMoney(double value)
    {
        var symbol = CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol;
        var abs = Math.Abs(value);
        var sign = value < 0 ? "-" : "";
        return abs switch
        {
            >= 1_000_000 => $"{sign}{symbol}{abs / 1_000_000:0.#}M",
            >= 1_000 => $"{sign}{symbol}{abs / 1_000:0.#}k",
            _ => $"{sign}{symbol}{abs:0}"
        };
    }

    public static string Percent(decimal? ratio) =>
        ratio is { } r ? r.ToString("P0", CultureInfo.CurrentCulture) : None;

    // FOOD_AND_DRINK -> Food and drink
    public static string Category(string category)
    {
        if (string.IsNullOrEmpty(category)) return category;
        var words = category.Replace('_', ' ').ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    // yyyy-MM -> "Mar"
    public static string ShortMonth(string month) =>
        DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("MMM", CultureInfo.CurrentCulture)
            : month;
}
