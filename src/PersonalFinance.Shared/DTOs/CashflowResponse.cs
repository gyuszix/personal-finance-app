namespace PersonalFinance.Shared.DTOs;

public class CashflowResponse
{
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net { get; set; }
}
