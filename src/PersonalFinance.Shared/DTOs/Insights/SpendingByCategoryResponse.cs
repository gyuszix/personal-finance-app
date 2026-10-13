namespace PersonalFinance.Shared.DTOs.Insights;

public class SpendingByCategoryResponse
{
    public string Month { get; set; } = string.Empty;   // yyyy-MM
    public decimal Total { get; set; }
    public List<CategorySpend> Categories { get; set; } = [];   // largest first
}

public class CategorySpend
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    // Of the month's total, 0-1; null when nothing was spent.
    public decimal? Share { get; set; }
}
