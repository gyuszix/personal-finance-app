using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Services;
using PersonalFinance.Shared.DTOs;
using static PersonalFinance.Tests.FakePlaid;

namespace PersonalFinance.Tests;

// Categorisation rules re-categorise a user's transactions everywhere -
// lists, summaries, insights - and removing one restores Plaid's category (#124).
public class CategoryRuleTests(FakePlaidWebApplicationFactory factory) : IClassFixture<FakePlaidWebApplicationFactory>
{
    private static Task<HttpResponseMessage> CreateAsync(TestUser user, object rule) =>
        user.Client.PostAsJsonAsync("/api/v1/rules", rule);

    private static async Task<CategoryRuleResponse> CreatedAsync(TestUser user, object rule)
    {
        var response = await CreateAsync(user, rule);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CategoryRuleResponse>())!;
    }

    private static async Task<Dictionary<int, TransactionResponse>> TransactionsAsync(TestUser user) =>
        (await user.Client.GetFromJsonAsync<PagedResult<TransactionResponse>>("/api/v1/transactions?pageSize=200"))!
            .Items.ToDictionary(t => t.TransactionId);

    [Fact]
    public async Task ARule_RecategorisesMatchingTransactions_Everywhere()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var gym = await user.AddTransactionAsync(account, 45m, "GENERAL_MERCHANDISE", description: "PURE GYM LONDON 0123");
        var other = await user.AddTransactionAsync(account, 12m, "GENERAL_MERCHANDISE", description: "Corner Shop");
        // Prime the cached summary so the test also proves it's invalidated.
        await user.Client.GetFromJsonAsync<List<TransactionSummaryResponse>>("/api/v1/transactions/summary");

        var rule = await CreatedAsync(user, new { pattern = "pure gym", matchType = "contains", category = "PERSONAL_CARE" });

        Assert.Equal(1, rule.MatchedTransactions);
        var transactions = await TransactionsAsync(user);
        Assert.Equal("PERSONAL_CARE", transactions[gym.TransactionId].CategoryPrimary);
        Assert.Equal("GENERAL_MERCHANDISE", transactions[gym.TransactionId].PlaidCategoryPrimary);
        Assert.Equal(rule.Id, transactions[gym.TransactionId].CategoryRuleId);
        Assert.Equal("GENERAL_MERCHANDISE", transactions[other.TransactionId].CategoryPrimary);
        Assert.Null(transactions[other.TransactionId].CategoryRuleId);

        var summary = await user.Client.GetFromJsonAsync<List<TransactionSummaryResponse>>("/api/v1/transactions/summary");
        Assert.Equal(45m, summary!.Single(s => s.Category == "PERSONAL_CARE").Total);
        Assert.Equal(12m, summary.Single(s => s.Category == "GENERAL_MERCHANDISE").Total);
    }

    [Fact]
    public async Task DeletingARule_RestoresPlaidsCategory()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var t = await user.AddTransactionAsync(account, 9m, "FOOD_AND_DRINK", description: "Netflix.com");
        var rule = await CreatedAsync(user, new { pattern = "netflix", matchType = "contains", category = "ENTERTAINMENT" });

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/v1/rules/{rule.Id}")).StatusCode);

        var restored = (await TransactionsAsync(user))[t.TransactionId];
        Assert.Equal("FOOD_AND_DRINK", restored.CategoryPrimary);
        Assert.Null(restored.CategoryRuleId);
    }

    [Fact]
    public async Task TheLowestPriorityNumberWins_AndChangingItReapplies()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var t = await user.AddTransactionAsync(account, 30m, "GENERAL_MERCHANDISE", description: "AMAZON PRIME VIDEO");
        await CreatedAsync(user, new { pattern = "amazon", matchType = "contains", category = "GENERAL_MERCHANDISE", priority = 50 });
        var video = await CreatedAsync(user, new { pattern = "prime video", matchType = "contains", category = "ENTERTAINMENT", priority = 100 });
        Assert.Equal("GENERAL_MERCHANDISE", (await TransactionsAsync(user))[t.TransactionId].CategoryPrimary);

        var update = await user.Client.PutAsJsonAsync($"/api/v1/rules/{video.Id}",
            new { pattern = "prime video", matchType = "contains", category = "ENTERTAINMENT", priority = 10 });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("ENTERTAINMENT", (await TransactionsAsync(user))[t.TransactionId].CategoryPrimary);
    }

    [Fact]
    public async Task AccountAndAmountRange_NarrowARule()
    {
        var user = await TestUser.CreateAsync(factory);
        var card = await user.AddAccountAsync(accountType: "Credit");
        var checking = await user.AddAccountAsync();
        var big = await user.AddTransactionAsync(card, 120m, "GENERAL_MERCHANDISE", description: "TESCO STORES");
        var small = await user.AddTransactionAsync(card, 8m, "GENERAL_MERCHANDISE", description: "TESCO STORES");
        var elsewhere = await user.AddTransactionAsync(checking, 150m, "GENERAL_MERCHANDISE", description: "TESCO STORES");

        await CreatedAsync(user, new { pattern = "TESCO STORES", matchType = "equals", category = "FOOD_AND_DRINK", accountId = card.AccountId, minAmount = 50m });

        var transactions = await TransactionsAsync(user);
        Assert.Equal("FOOD_AND_DRINK", transactions[big.TransactionId].CategoryPrimary);
        Assert.Equal("GENERAL_MERCHANDISE", transactions[small.TransactionId].CategoryPrimary);
        Assert.Equal("GENERAL_MERCHANDISE", transactions[elsewhere.TransactionId].CategoryPrimary);
    }

    [Fact]
    public async Task Equals_MatchesTheWholeDescriptionOnly_IgnoringCase()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var exact = await user.AddTransactionAsync(account, 5m, description: "Uber");
        var longer = await user.AddTransactionAsync(account, 5m, description: "Uber Eats");

        await CreatedAsync(user, new { pattern = "UBER", matchType = "equals", category = "TRANSPORTATION" });

        var transactions = await TransactionsAsync(user);
        Assert.Equal("TRANSPORTATION", transactions[exact.TransactionId].CategoryPrimary);
        Assert.Equal("FOOD_AND_DRINK", transactions[longer.TransactionId].CategoryPrimary);
    }

    [Theory]
    [InlineData("""{"pattern":"x","matchType":"contains","category":"TRAVEL"}""", "pattern")]
    [InlineData("""{"pattern":"airline","matchType":"regex","category":"TRAVEL"}""", "matchType")]
    [InlineData("""{"pattern":"airline","matchType":"contains","category":"HOLIDAYS"}""", "category")]
    [InlineData("""{"pattern":"airline","matchType":"contains","category":"TRAVEL","minAmount":50,"maxAmount":10}""", "minAmount")]
    [InlineData("""{"pattern":"airline","matchType":"contains","category":"TRAVEL","priority":5000}""", "priority")]
    public async Task InvalidRules_AreRejected(string json, string field)
    {
        var user = await TestUser.CreateAsync(factory);

        var response = await user.Client.PostAsync("/api/v1/rules", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Rules_AreIsolatedBetweenUsers()
    {
        var alice = await TestUser.CreateAsync(factory);
        var bob = await TestUser.CreateAsync(factory);
        var aliceAccount = await alice.AddAccountAsync();
        var bobs = await bob.AddTransactionAsync(await bob.AddAccountAsync(), 20m, "GENERAL_MERCHANDISE", description: "Spotify");
        var rule = await CreatedAsync(alice, new { pattern = "spotify", matchType = "contains", category = "ENTERTAINMENT" });

        // Alice's rule doesn't touch Bob's transactions...
        Assert.Equal("GENERAL_MERCHANDISE", (await TransactionsAsync(bob))[bobs.TransactionId].CategoryPrimary);
        // ...and Bob can't see, change or delete it, or point a rule at Alice's account.
        Assert.Empty((await bob.Client.GetFromJsonAsync<List<CategoryRuleResponse>>("/api/v1/rules"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.PutAsJsonAsync($"/api/v1/rules/{rule.Id}",
            new { pattern = "spotify", matchType = "contains", category = "TRAVEL" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.DeleteAsync($"/api/v1/rules/{rule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(bob,
            new { pattern = "spotify", matchType = "contains", category = "TRAVEL", accountId = aliceAccount.AccountId })).StatusCode);
    }

    [Fact]
    public async Task Sync_AppliesRulesToNewTransactions_AndKeepsThemThroughPlaidUpdates()
    {
        var user = await TestUser.CreateAsync(factory);
        var plaidId = $"acc-{Guid.NewGuid():N}";
        var protector = factory.Services.GetRequiredService<PlaidTokenProtector>();
        var account = await user.AddAccountAsync(accessToken: protector.Protect("access-sandbox-rules", user.Id), plaidAccountId: plaidId);
        await CreatedAsync(user, new { pattern = "pure gym", matchType = "contains", category = "PERSONAL_CARE" });
        factory.Plaid.SyncPages(new()
        {
            [""] = Page("r1", added: [Txn($"gym-{plaidId}", plaidId, 45m, category: "GENERAL_MERCHANDISE", name: "PURE GYM")]),
            ["r1"] = Page("r2", modified: [Txn($"gym-{plaidId}", plaidId, 45m, category: "GENERAL_SERVICES", name: "PURE GYM")])
        });
        factory.Plaid.Balances("item-rules", (plaidId, 1m));

        async Task SyncAsync()
        {
            using var scope = factory.SystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var accounts = await db.Accounts.IgnoreQueryFilters().Where(a => a.AccountId == account.AccountId).ToListAsync();
            await scope.ServiceProvider.GetRequiredService<PlaidSyncService>().SyncItemAsync(accounts);
        }

        await SyncAsync();
        var added = (await TransactionsAsync(user)).Values.Single();
        Assert.Equal("PERSONAL_CARE", added.CategoryPrimary);
        Assert.Equal("GENERAL_MERCHANDISE", added.PlaidCategoryPrimary);

        await SyncAsync(); // Plaid changes its mind about the category
        var modified = (await TransactionsAsync(user)).Values.Single();
        Assert.Equal("PERSONAL_CARE", modified.CategoryPrimary);
        Assert.Equal("GENERAL_SERVICES", modified.PlaidCategoryPrimary);
    }

    [Fact]
    public async Task Rules_RequireSignIn() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/rules")).StatusCode);
}
