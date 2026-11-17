namespace PersonalFinance.Api.Entities;
public class Transaction
{
    public int TransactionId {get; set;}
    public int AccountId {get; set;}
    public decimal Amount {get; set;}
    public string Description {get; set;}
    public DateTime Date {get; set;}
    public string PlaidTransactionId {get; set;}

    public string UserId {get; set;}

    public bool IsPending {get; set;}
    public string? PendingTransactionId {get; set;}
    // The effective category - Plaid's, unless one of the user's rules
    // matched (#124). What insights, budgets and filters read.
    public string? CategoryPrimary {get; set;}
    // What Plaid said, kept so removing a rule restores it.
    public string? PlaidCategoryPrimary {get; set;}
    // The rule that set CategoryPrimary, if any.
    public int? CategoryRuleId {get; set;}
    public string? CategoryDetailed {get; set;}
}
