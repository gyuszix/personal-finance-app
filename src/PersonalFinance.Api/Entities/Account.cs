namespace PersonalFinance.Api.Entities;
public class Account
{
    public int AccountId {get; set;}
    public string UserId {get; set;}
    public decimal Balance {get; set;}
    public string BankName {get; set;}
    public string AccountType {get; set;}
    // Plaid's subtype (Checking, Savings, CreditCard, Mortgage, ...). Null for
    // accounts linked before it was stored, until their next sync (#83).
    public string? Subtype {get; set;}
    public string PlaidAccountId {get; set;}
    // The Plaid Item (bank connection) this account belongs to. Webhooks
    // identify the Item, not the account. Null for accounts linked before
    // it was stored, until their next sync (#114).
    public string? PlaidItemId {get; set;}
    public string PlaidAccessToken {get; set;}
    public string? SyncCursor {get; set;}
}
