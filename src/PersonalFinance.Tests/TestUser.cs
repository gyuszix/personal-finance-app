using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Tests;

// A registered, signed-in user with an authenticated client, plus helpers to
// seed their data straight into the database (bypassing Plaid).
public sealed class TestUser(TestWebApplicationFactory factory, string id, HttpClient client, string accessToken)
{
    public string Id { get; } = id;
    public HttpClient Client { get; } = client;
    public string AccessToken { get; } = accessToken;

    public static async Task<TestUser> CreateAsync(TestWebApplicationFactory factory)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        const string password = "Test123!";

        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password })).EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<Tokens>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.Token);

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email);

        return new TestUser(factory, user!.Id, client, tokens.Token);
    }

    public async Task<Account> AddAccountAsync(
        decimal balance = 100m, string accountType = "Depository", string accessToken = "protected-token")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account
        {
            UserId = Id,
            BankName = "Test Bank",
            AccountType = accountType,
            Balance = balance,
            PlaidAccountId = Guid.NewGuid().ToString(),
            PlaidAccessToken = accessToken
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    public async Task<Transaction> AddTransactionAsync(
        Account account, decimal amount, string? category = "FOOD_AND_DRINK", DateTime? date = null, bool pending = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transaction = new Transaction
        {
            AccountId = account.AccountId,
            UserId = Id,
            Amount = amount,
            Description = "Test transaction",
            Date = date ?? DateTime.UtcNow,
            PlaidTransactionId = Guid.NewGuid().ToString(),
            IsPending = pending,
            CategoryPrimary = category
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction;
    }

    private record Tokens(string Token, string RefreshToken);
}
