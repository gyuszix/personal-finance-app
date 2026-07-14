namespace PersonalFinance.Api.Entities;
public class User
{
    public int UserId {get; set;}
    public string Email {get; set;}
    public string PasswordHash {get; set;}
}