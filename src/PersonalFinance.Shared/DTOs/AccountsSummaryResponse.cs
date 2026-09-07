namespace PersonalFinance.Shared.DTOs;

public class AccountsSummaryResponse
{
    public decimal TotalBalance { get; set; }
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    public decimal NetWorth { get; set; }
    public List<AccountResponse> Accounts { get; set; } = [];
}
