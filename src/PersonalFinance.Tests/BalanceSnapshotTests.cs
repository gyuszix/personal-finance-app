using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Services;

namespace PersonalFinance.Tests;

// One balance per account per day, recorded on every sync (#84).
public class BalanceSnapshotTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Record_TwiceInADay_KeepsOneSnapshotWithTheLatestBalance()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync(balance: 100m);

        await RecordAsync(account.AccountId, balance: 100m);
        await RecordAsync(account.AccountId, balance: 250m);

        var snapshots = await SnapshotsForAsync(account.AccountId);
        var snapshot = Assert.Single(snapshots);
        Assert.Equal(250m, snapshot.Balance);
        Assert.Equal(user.Id, snapshot.UserId);
    }

    [Fact]
    public async Task WithoutSignedInUser_SnapshotFilterMatchesNothing()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await RecordAsync(account.AccountId, balance: 100m);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.BalanceSnapshots.ToListAsync());
        Assert.NotEmpty(await SnapshotsForAsync(account.AccountId));
    }

    [Fact]
    public async Task Unlink_RemovesTheBanksSnapshots()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync(accessToken: "bank-snapshots");
        await RecordAsync(account.AccountId, balance: 100m);

        (await user.Client.DeleteAsync($"/api/v1/accounts/{account.AccountId}")).EnsureSuccessStatusCode();

        Assert.Empty(await SnapshotsForAsync(account.AccountId));
    }

    // Records the way PlaidSyncService does: the account as tracked, with its
    // refreshed balance, saved together.
    private async Task RecordAsync(int accountId, decimal balance)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(a => a.AccountId == accountId);
        account.Balance = balance;
        await scope.ServiceProvider.GetRequiredService<BalanceSnapshotService>().RecordAsync([account]);
        await db.SaveChangesAsync();
    }

    private async Task<List<BalanceSnapshot>> SnapshotsForAsync(int accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.BalanceSnapshots.IgnoreQueryFilters().Where(s => s.AccountId == accountId).ToListAsync();
    }
}
