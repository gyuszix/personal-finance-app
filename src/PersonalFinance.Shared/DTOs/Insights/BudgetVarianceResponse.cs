namespace PersonalFinance.Shared.DTOs.Insights;

public class BudgetVarianceResponse
{
    public string Month { get; set; } = string.Empty;
    public decimal TotalLimit { get; set; }
    public decimal TotalSpent { get; set; }
    // TotalSpent ÷ TotalLimit; null when every limit is zero.
    public decimal? TotalUsed { get; set; }
    public int OverBudget { get; set; }
    // Over budget first (most over first), then by share of the limit used.
    public List<BudgetVarianceLine> Categories { get; set; } = [];
}

public class BudgetVarianceLine
{
    public string Category { get; set; } = string.Empty;
    public decimal Spent { get; set; }
    public decimal Limit { get; set; }
    // Limit is a suggestion from recent spending, not one the user saved.
    public bool IsSuggested { get; set; }
    // Limit − Spent; negative when over.
    public decimal Remaining { get; set; }
    // Spent ÷ Limit; null with a zero limit.
    public decimal? Used { get; set; }
}
