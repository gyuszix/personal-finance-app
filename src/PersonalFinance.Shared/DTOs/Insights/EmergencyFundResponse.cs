namespace PersonalFinance.Shared.DTOs.Insights;

public class EmergencyFundResponse
{
    public string Month { get; set; } = string.Empty;
    // Today's checking, savings and similar balances.
    public decimal LiquidCash { get; set; }
    public int LiquidAccounts { get; set; }
    // See BurnRateResponse.MonthlyBurnRate; null with no spending history.
    public decimal? MonthlyBurnRate { get; set; }
    // LiquidCash ÷ MonthlyBurnRate; null without a burn rate.
    public decimal? MonthsCovered { get; set; }
}
