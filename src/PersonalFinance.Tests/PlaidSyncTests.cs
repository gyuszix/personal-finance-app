using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Services;
using static PersonalFinance.Tests.FakePlaid;

namespace PersonalFinance.Tests;

// Sync must store each Plaid transaction exactly once however its pages
// arrive: replayed, modified before we saw it, or fetched by two syncs at
// once - and must save the cursor only together with what it produced (#114).
public class PlaidSyncTests(FakePlaidWebApplicationFactory factory) : IClassFixture<FakePlaidWebApplicationFactory>
{
    private FakePlaid Plaid => factory.Plaid;

    private async Task<(TestUser User, Account Account, string PlaidAccountId)> LinkedAccountAsync()
    {
        var user = await TestUser.CreateAsync(factory);
        var plaidAccountId = $"acc-{Guid.NewGuid():N}";
        var protector = factory.Services.GetRequiredService<PlaidTokenProtector>();
        var account = await user.AddAccountAsync(
            accessToken: protector.Protect($"access-sandbox-{Guid.NewGuid():N}"), plaidAccountId: plaidAccountId);
        Plaid.Balances("item-1", (plaidAccountId, 123.45m));
        return (user, account, plaidAccountId);
    }

    private async Task<(int Added, int Modified, int Removed)> SyncAsync(int accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sync = scope.ServiceProvider.GetRequiredService<PlaidSyncService>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(a => a.AccountId == accountId);
        return await sync.SyncItemAsync([account]);
    }

    private async Task<List<Transaction>> StoredAsync(int accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Transactions.IgnoreQueryFilters().Where(t => t.AccountId == accountId).OrderBy(t => t.PlaidTransactionId).ToListAsync();
    }

    private async Task<Account> ReloadAsync(int accountId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Accounts.IgnoreQueryFilters().SingleAsync(a => a.AccountId == accountId);
    }

    [Fact]
    public async Task Sync_StoresTransactions_CursorBalanceAndItemId()
    {
        var (_, account, plaidId) = await LinkedAccountAsync();
        Plaid.SyncPages(new()
        {
            [""] = Page("c1", added: [Txn("t1", plaidId, 4.50m), Txn("t2", plaidId, -1000m, category: "INCOME")])
        });

        var result = await SyncAsync(account.AccountId);

        Assert.Equal((2, 0, 0), result);
        Assert.Equal([4.50m, -1000m], (await StoredAsync(account.AccountId)).Select(t => t.Amount));
        var saved = await ReloadAsync(account.AccountId);
        Assert.Equal("c1", saved.SyncCursor);
        Assert.Equal(123.45m, saved.Balance);
        Assert.Equal("item-1", saved.PlaidItemId);
    }

    [Fact]
    public async Task ReplayedPage_StoresEachTransactionOnce()
    {
        var (_, account, plaidId) = await LinkedAccountAsync();
        Plaid.SyncPages(new() { [""] = Page("c1", added: [Txn("r1", plaidId, 10m), Txn("r2", plaidId, 20m)]) });
        await SyncAsync(account.AccountId);

        // Lose the cursor, as a restored backup or an account added to the
        // Item later would: Plaid replays everything from the start.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Accounts.IgnoreQueryFilters().Where(a => a.AccountId == account.AccountId)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.SyncCursor, (string?)null));
        }
        var replay = await SyncAsync(account.AccountId);

        Assert.Equal((0, 0, 0), replay);
        Assert.Equal(["r1", "r2"], (await StoredAsync(account.AccountId)).Select(t => t.PlaidTransactionId));
    }

    [Fact]
    public async Task ModificationOfATransactionNeverSeen_IsStored()
    {
        var (_, account, plaidId) = await LinkedAccountAsync();
        Plaid.SyncPages(new() { [""] = Page("c1", modified: [Txn("m1", plaidId, 7.25m)]) });

        var result = await SyncAsync(account.AccountId);

        Assert.Equal((1, 0, 0), result);
        Assert.Equal(7.25m, Assert.Single(await StoredAsync(account.AccountId)).Amount);
    }

    [Fact]
    public async Task Modification_UpdatesTheStoredRow()
    {
        var (_, account, plaidId) = await LinkedAccountAsync();
        Plaid.SyncPages(new()
        {
            [""] = Page("c1", added: [Txn("u1", plaidId, 5m, pending: true)]),
            ["c1"] = Page("c2", modified: [Txn("u1", plaidId, 5.75m, category: "ENTERTAINMENT")])
        });
        await SyncAsync(account.AccountId);

        var result = await SyncAsync(account.AccountId);

        Assert.Equal((0, 1, 0), result);
        var row = Assert.Single(await StoredAsync(account.AccountId));
        Assert.Equal(5.75m, row.Amount);
        Assert.Equal("ENTERTAINMENT", row.CategoryPrimary);
    }

    [Fact]
    public async Task PendingThenPosted_AcrossPagesOfOneSync_LeavesOnlyThePostedRow()
    {
        var (_, account, plaidId) = await LinkedAccountAsync();
        Plaid.SyncPages(new()
        {
            [""] = Page("p1", added: [Txn("pending-1", plaidId, 12m, pending: true)], hasMore: true),
            ["p1"] = Page("p2", added: [Txn("posted-1", plaidId, 12m)], removed: ["pending-1"])
        });

        var result = await SyncAsync(account.AccountId);

        Assert.Equal((2, 0, 1), result);
        var row = Assert.Single(await StoredAsync(account.AccountId));
        Assert.Equal("posted-1", row.PlaidTransactionId);
        Assert.False(row.IsPending);
    }

    [Fact]
    public async Task ConcurrentSyncsOfOneItem_ApplyEachPageOnce()
    {
        var (_, account, plaidId) = await LinkedAccountAsync();
        var firstFetchStarted = new TaskCompletionSource();
        var releaseFirstFetch = new TaskCompletionSource();
        var calls = 0;
        Plaid.OnAsync("/transactions/sync", async body =>
        {
            var cursor = body["cursor"]?.GetValue<string>();
            if (cursor is null)
            {
                // Hold the first syncer mid-fetch, so the second one starts
                // while the first still has the original cursor.
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstFetchStarted.SetResult();
                    await releaseFirstFetch.Task;
                }
                return Page("c1", added: [Txn("cc1", plaidId, 1m), Txn("cc2", plaidId, 2m)]);
            }
            return Page("c1");
        });

        var first = SyncAsync(account.AccountId);
        await firstFetchStarted.Task;
        var second = SyncAsync(account.AccountId);
        await Task.Delay(300); // the second is now blocked on the row lock, not fetching
        releaseFirstFetch.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(2, results.Sum(r => r.Added));
        Assert.Equal(["cc1", "cc2"], (await StoredAsync(account.AccountId)).Select(t => t.PlaidTransactionId));
        // The second sync waited and continued from the first one's cursor.
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailureMidSync_SavesNothing_AndKeepsTheOldCursor()
    {
        var (_, account, plaidId) = await LinkedAccountAsync();
        Plaid.SyncPages(new()
        {
            [""] = Page("half", added: [Txn("f1", plaidId, 1m)], hasMore: true)
            // "half" -> not faked: Plaid errors on the second page
        });

        await Assert.ThrowsAnyAsync<Exception>(() => SyncAsync(account.AccountId));

        Assert.Empty(await StoredAsync(account.AccountId));
        Assert.Null((await ReloadAsync(account.AccountId)).SyncCursor);
    }
}
