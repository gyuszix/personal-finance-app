using System.Net;
using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Tests;

// DELETE /accounts/{id} removes the whole Plaid Item behind the account (#64).
// The seeded access tokens were never encrypted, so these exercise the
// "token no longer decrypts" path - Plaid itself isn't called.
public class UnlinkAccountTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Unlink_RemovesEveryAccountOfThatBankAndTheirTransactions()
    {
        var user = await TestUser.CreateAsync(factory);
        var checking = await user.AddAccountAsync(balance: 100m, accessToken: "bank-a");
        var savings = await user.AddAccountAsync(balance: 200m, accessToken: "bank-a");
        var otherBank = await user.AddAccountAsync(balance: 300m, accessToken: "bank-b");
        await user.AddTransactionAsync(checking, 10m);
        await user.AddTransactionAsync(savings, 20m);
        var kept = await user.AddTransactionAsync(otherBank, 30m);

        // Prime the cached summary so the test also proves it's invalidated.
        await user.Client.GetFromJsonAsync<AccountsSummaryResponse>("/api/v1/accounts/summary");

        var response = await user.Client.DeleteAsync($"/api/v1/accounts/{savings.AccountId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var summary = await user.Client.GetFromJsonAsync<AccountsSummaryResponse>("/api/v1/accounts/summary");
        Assert.Equal([otherBank.AccountId], summary!.Accounts.Select(a => a.AccountId));
        var transactions = await user.Client.GetFromJsonAsync<PagedResult<TransactionResponse>>("/api/v1/transactions");
        Assert.Equal([kept.TransactionId], transactions!.Items.Select(t => t.TransactionId));
    }

    [Fact]
    public async Task Unlink_SomeoneElsesAccount_IsNotFoundAndKeepsIt()
    {
        var owner = await TestUser.CreateAsync(factory);
        var intruder = await TestUser.CreateAsync(factory);
        var account = await owner.AddAccountAsync();

        var response = await intruder.Client.DeleteAsync($"/api/v1/accounts/{account.AccountId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var summary = await owner.Client.GetFromJsonAsync<AccountsSummaryResponse>("/api/v1/accounts/summary");
        Assert.Single(summary!.Accounts);
    }
}
