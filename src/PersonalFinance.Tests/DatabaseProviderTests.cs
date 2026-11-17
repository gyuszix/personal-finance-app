using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Tests;

// Guards #111: the suite must run on real Postgres with the real migrations,
// not a stand-in provider that hides Postgres-only behaviour.
public class DatabaseProviderTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Tests_RunOnPostgres_WithEveryMigrationApplied()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var version = await db.Database.SqlQueryRaw<string>("SELECT version() AS \"Value\"").SingleAsync();
        Assert.StartsWith("PostgreSQL 16", version);
    }
}
