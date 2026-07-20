namespace PersonalFinance.Shared.DTOs;

public class AccountResponse
{
    public int AccountId { get; set; }
    public string BankName { get; set; }
    public string AccountType { get; set; }
    public decimal Balance { get; set; }
}
