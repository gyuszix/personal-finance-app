namespace PersonalFinance.Shared.DTOs.Insights;

public class SavingsRateResponse
{
    public string Month { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public decimal Spending { get; set; }
    // (income − spending) ÷ income; negative if overspent, null with no income.
    public decimal? SavingsRate { get; set; }
    // 12 months ending with Month, oldest first.
    public List<SavingsRateMonth> Months { get; set; } = [];
}

public class SavingsRateMonth
{
    public string Month { get; set; } = string.Empty;
    public decimal? SavingsRate { get; set; }
}
