namespace PersonalFinance.Shared.DTOs.Insights;

public class CashflowTrendResponse
{
    // The 12 months ending with the requested one, oldest first.
    public List<CashflowMonth> Months { get; set; } = [];
}

public class CashflowMonth
{
    public string Month { get; set; } = string.Empty;   // yyyy-MM
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net { get; set; }
}
