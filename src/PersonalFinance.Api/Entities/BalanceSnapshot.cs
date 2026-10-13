namespace PersonalFinance.Api.Entities;

// An account's balance at the end of a day, one row per account per day.
// Plaid only reports today's balance, so this is the only history the net
// worth trend has (#84).
public class BalanceSnapshot
{
    public int BalanceSnapshotId { get; set; }
    public int AccountId { get; set; }
    public string UserId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Balance { get; set; }
}
