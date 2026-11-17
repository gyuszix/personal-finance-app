using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Tests;

// Row-level security as the second layer: with the EF query filters
// deliberately bypassed, Postgres itself still keeps users apart (#119).
public class RowLevelSecurityTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    // Makes code below run as if inside a request signed in as userId, the
    // way RlsSessionInterceptor sees a real request.
    private static IDisposable SignedInAs(IServiceProvider services, string? userId)
    {
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        var identity = userId is null ? new ClaimsIdentity() : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test");
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new Restore(() => accessor.HttpContext = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private async Task<(TestUser Alice, TestUser Bob)> TwoUsersWithDataAsync()
    {
        var alice = await TestUser.CreateAsync(factory);
        var bob = await TestUser.CreateAsync(factory);
        foreach (var user in new[] { alice, bob })
        {
            var account = await user.AddAccountAsync();
            await user.AddTransactionAsync(account, 10m);
            using var scope = factory.SystemScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BalanceSnapshots.Add(new BalanceSnapshot { AccountId = account.AccountId, UserId = user.Id, Date = DateOnly.FromDateTime(DateTime.UtcNow), Balance = 1m });
            db.Budgets.Add(new Budget { UserId = user.Id, Category = "FOOD_AND_DRINK", MonthlyLimit = 100m });
            db.CategoryRules.Add(new CategoryRule { UserId = user.Id, Pattern = "gym", MatchType = "contains", Category = "PERSONAL_CARE", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        return (alice, bob);
    }

    [Fact]
    public async Task TheApiRole_IsSubjectToRowLevelSecurity()
    {
        using var scope = factory.SystemScope();
        var posture = await DatabaseRoleCheck.GetAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        Assert.Equal("pf_app", posture.Role);
        Assert.True(posture.IsSafe, $"superuser={posture.IsSuperuser} bypassrls={posture.BypassesRls} owner={posture.OwnsTables}");
    }

    [Fact]
    public async Task WithTheQueryFiltersIgnored_AUserStillSeesOnlyTheirOwnRows()
    {
        var (alice, _) = await TwoUsersWithDataAsync();

        using var scope = factory.Services.CreateScope();
        using var _ = SignedInAs(scope.ServiceProvider, alice.Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.All(await db.Accounts.IgnoreQueryFilters().ToListAsync(), a => Assert.Equal(alice.Id, a.UserId));
        Assert.All(await db.Transactions.IgnoreQueryFilters().ToListAsync(), t => Assert.Equal(alice.Id, t.UserId));
        Assert.All(await db.BalanceSnapshots.IgnoreQueryFilters().ToListAsync(), s => Assert.Equal(alice.Id, s.UserId));
        Assert.All(await db.Budgets.IgnoreQueryFilters().ToListAsync(), b => Assert.Equal(alice.Id, b.UserId));
        Assert.All(await db.CategoryRules.IgnoreQueryFilters().ToListAsync(), r => Assert.Equal(alice.Id, r.UserId));
        Assert.NotEmpty(await db.CategoryRules.IgnoreQueryFilters().ToListAsync());
        Assert.NotEmpty(await db.Transactions.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task RawSql_IsIsolatedToo()
    {
        var (alice, bob) = await TwoUsersWithDataAsync();

        using var scope = factory.Services.CreateScope();
        using var _ = SignedInAs(scope.ServiceProvider, alice.Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var owners = await db.Database.SqlQueryRaw<string>("""SELECT DISTINCT "UserId" AS "Value" FROM "Transactions" """).ToListAsync();
        Assert.Equal([alice.Id], owners);
        Assert.Empty(await db.Database.SqlQueryRaw<int>($"""SELECT "AccountId" AS "Value" FROM "Accounts" WHERE "UserId" = '{bob.Id}'""").ToListAsync());
    }

    [Fact]
    public async Task AUser_CantWriteRowsOwnedBySomeoneElse()
    {
        var (alice, bob) = await TwoUsersWithDataAsync();

        using var scope = factory.Services.CreateScope();
        using var _ = SignedInAs(scope.ServiceProvider, alice.Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Budgets.Add(new Budget { UserId = bob.Id, Category = "TRAVEL", MonthlyLimit = 1m });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("row-level security", error.InnerException!.Message);

        // And can't touch theirs: the UPDATE matches no visible rows.
        var changed = await db.Budgets.IgnoreQueryFilters().Where(b => b.UserId == bob.Id)
            .ExecuteUpdateAsync(b => b.SetProperty(x => x.MonthlyLimit, 0m));
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task WithNoUserAndNoSystemScope_NothingIsVisible_AndNothingCanBeWritten()
    {
        await TwoUsersWithDataAsync();

        using var scope = factory.Services.CreateScope(); // not SystemScope()
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.Accounts.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.Transactions.IgnoreQueryFilters().ToListAsync());
        db.Budgets.Add(new Budget { UserId = "anyone", Category = "TRAVEL", MonthlyLimit = 1m });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task TheSystemScope_IsIgnoredInsideARequest()
    {
        var (alice, _) = await TwoUsersWithDataAsync();

        using var scope = factory.SystemScope();
        using var _ = SignedInAs(scope.ServiceProvider, alice.Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.All(await db.Accounts.IgnoreQueryFilters().ToListAsync(), a => Assert.Equal(alice.Id, a.UserId));
    }

    [Fact]
    public async Task AnAnonymousRequest_SeesNothing_EvenInsideASystemScope()
    {
        await TwoUsersWithDataAsync();

        using var scope = factory.SystemScope();
        using var _ = SignedInAs(scope.ServiceProvider, null);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.Accounts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task AuditEvents_AreReadableOnlyByTheirUser_ButAnyoneCanAppend()
    {
        var (alice, bob) = await TwoUsersWithDataAsync();

        using var scope = factory.Services.CreateScope();
        using var _ = SignedInAs(scope.ServiceProvider, alice.Id);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.All(await db.AuditEvents.ToListAsync(), e => Assert.Equal(alice.Id, e.UserId));
        Assert.False(await db.AuditEvents.AnyAsync(e => e.UserId == bob.Id));
        await scope.ServiceProvider.GetRequiredService<PersonalFinance.Api.Services.AuditLog>()
            .RecordAsync(AuditEventTypes.LoginFailed, null, new { reason = "test" });
    }

    [Fact]
    public async Task TheApiRole_CantChangeTheSchema()
    {
        using var scope = factory.SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var create = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("""CREATE TABLE "Smuggled" (id int)"""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, create.SqlState);
        var disable = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Transactions" DISABLE ROW LEVEL SECURITY"""));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, disable.SqlState);
    }
}
