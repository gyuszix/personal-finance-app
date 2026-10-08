using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Tests;

// One user must never see another's accounts or transactions, through any
// read endpoint.
public class IsolationTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task ReadEndpoints_OnlyReturnTheCallersData()
    {
        var alice = await TestUser.CreateAsync(factory);
        var bob = await TestUser.CreateAsync(factory);
        var aliceAccount = await alice.AddAccountAsync(balance: 1000m);
        await alice.AddTransactionAsync(aliceAccount, 50m);

        var transactions = await bob.Client.GetFromJsonAsync<PagedResult<TransactionResponse>>("/api/v1/transactions");
        var summary = await bob.Client.GetFromJsonAsync<List<TransactionSummaryResponse>>("/api/v1/transactions/summary");
        var cashflow = await bob.Client.GetFromJsonAsync<CashflowResponse>("/api/v1/transactions/cashflow");
        var accounts = await bob.Client.GetFromJsonAsync<AccountsSummaryResponse>("/api/v1/accounts/summary");

        Assert.Equal(0, transactions!.TotalCount);
        Assert.Empty(summary!);
        Assert.Equal(0m, cashflow!.Expenses);
        Assert.Empty(accounts!.Accounts);
        Assert.Equal(0m, accounts.TotalBalance);
    }
}
