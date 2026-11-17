using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Tests;

// The indexes and foreign keys from docs/DB_INDEXES.md (#113).
public class SchemaIntegrityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData("Transactions", "IX_Transactions_UserId_Date", "(\"UserId\", \"Date\")")]
    [InlineData("Transactions", "IX_Transactions_AccountId", "(\"AccountId\")")]
    [InlineData("Accounts", "IX_Accounts_UserId", "(\"UserId\")")]
    public async Task HotPathIndexes_Exist(string table, string index, string columns)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var definition = await db.Database.SqlQueryRaw<string>(
            "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = {0} AND indexname = {1}", table, index)
            .SingleOrDefaultAsync();

        Assert.NotNull(definition);
        Assert.EndsWith(columns, definition);
    }

    [Fact]
    public async Task Transaction_ForAnAccountThatDoesntExist_IsRejected()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Transactions.Add(new Transaction
        {
            AccountId = int.MaxValue, UserId = "nobody", Amount = 1m, Description = "orphan",
            Date = DateTime.UtcNow, PlaidTransactionId = Guid.NewGuid().ToString()
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingAnAccount_CascadesToItsTransactionsAndSnapshots()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 10m);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.BalanceSnapshots.Add(new BalanceSnapshot
        {
            AccountId = account.AccountId, UserId = user.Id, Date = DateOnly.FromDateTime(DateTime.UtcNow), Balance = 1m
        });
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlAsync($"DELETE FROM \"Accounts\" WHERE \"AccountId\" = {account.AccountId}");

        Assert.False(await db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.AccountId == account.AccountId));
        Assert.False(await db.BalanceSnapshots.IgnoreQueryFilters().AnyAsync(s => s.AccountId == account.AccountId));
    }
}
