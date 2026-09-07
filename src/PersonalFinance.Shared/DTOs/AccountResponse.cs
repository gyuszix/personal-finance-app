namespace PersonalFinance.Shared.DTOs;

public class AccountResponse
{
    public int AccountId { get; set; }
    public string BankName { get; set; }
    public string AccountType { get; set; }
    public decimal Balance { get; set; }

    // "Asset", "Liability", or "Other" (unrecognized Plaid account type -
    // excluded from net worth math rather than guessed at)
    public string Classification { get; set; } = "Other";
}
