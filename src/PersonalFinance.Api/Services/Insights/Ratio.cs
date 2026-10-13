namespace PersonalFinance.Api.Services.Insights;

public static class Ratio
{
    // numerator / denominator, or null when the denominator is zero - the
    // app shows that as "—" instead of a crash, infinity or a fake 0%.
    public static decimal? Of(decimal numerator, decimal denominator) =>
        denominator == 0 ? null : Math.Round(numerator / denominator, 4);
}
