namespace PersonalFinance.Shared.DTOs.Insights;

public class BurnRateResponse
{
    public string Month { get; set; } = string.Empty;
    // Average monthly spending over WindowStart..WindowEnd (three complete
    // months); null with no spending in that window.
    public decimal? MonthlyBurnRate { get; set; }
    public string WindowStart { get; set; } = string.Empty;
    public string WindowEnd { get; set; } = string.Empty;
    public decimal SpentSoFar { get; set; }
    public int DaysElapsed { get; set; }
    public int DaysInMonth { get; set; }
    // SpentSoFar ÷ DaysElapsed × DaysInMonth; for a past month, what was spent.
    public decimal ProjectedSpending { get; set; }
    // ProjectedSpending ÷ MonthlyBurnRate; null without a burn rate.
    public decimal? PaceVsBurnRate { get; set; }
}
