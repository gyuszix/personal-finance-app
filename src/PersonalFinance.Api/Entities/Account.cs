namespace PersonalFinance.Api.Entities;
public class Account
{
    public int AccountId {get; set;}
    public int UserId {get; set;}
    public decimal Balance {get; set;}
    public string BankName {get; set;}
    public string AccountType {get; set;}
    public string PlaidAccountId {get; set;}
    public string PlaidAccessToken {get; set;}

}