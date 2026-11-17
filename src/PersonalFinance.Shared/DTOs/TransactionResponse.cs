namespace PersonalFinance.Shared.DTOs;

public class TransactionResponse
{
    public int TransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; }
    public DateTime Date { get; set; }
    public string? CategoryPrimary { get; set; }

    // Set when one of the user's rules re-categorised it (#124): what Plaid
    // said, and which rule changed it.
    public string? PlaidCategoryPrimary { get; set; }
    public int? CategoryRuleId { get; set; }
}
