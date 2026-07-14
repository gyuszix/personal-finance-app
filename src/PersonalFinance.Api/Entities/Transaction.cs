namespace PersonalFinance.Api.Entities;
public class Transaction
{
    public int TransactionId {get; set;}
    public int AccountId {get; set;}
    public decimal Amount {get; set;}
    public string Description {get; set;}
    public DateTime Date {get; set;}
    public string PlaidTransactionId {get; set;}
}