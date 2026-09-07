namespace PersonalFinance.Shared.DTOs;

public class TransactionSummaryResponse
{
    public string Category { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int TransactionCount { get; set; }
}
