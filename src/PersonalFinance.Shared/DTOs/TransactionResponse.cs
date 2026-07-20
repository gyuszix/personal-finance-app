namespace PersonalFinance.Shared.DTOs;

public class TransactionResponse
{
    public int TransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; }
    public DateTime Date { get; set; }
}
