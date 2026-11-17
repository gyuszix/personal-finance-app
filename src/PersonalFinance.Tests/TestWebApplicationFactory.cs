using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace PersonalFinance.Tests;

// One Postgres container for the whole test run, started on first use and
// removed by Testcontainers' reaper when the run ends (#111).
public static class PostgresContainer
{
    private static readonly Lazy<PostgreSqlContainer> Instance = new(() =>
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        container.StartAsync().GetAwaiter().GetResult();

        // The API's role, as an operator would create it (README, "Database
        // roles"). The migrations grant it access and leave it subject to
        // row-level security, which is what the tests then run as (#119).
        using var admin = new NpgsqlConnection(container.GetConnectionString());
        admin.Open();
        using var role = new NpgsqlCommand($"CREATE ROLE pf_app LOGIN PASSWORD '{AppRolePassword}'", admin);
        role.ExecuteNonQuery();
        return container;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    public const string AppRolePassword = "pf_app_test";

    // A new, empty database on the shared container. Test classes run in
    // parallel, one factory each, so each needs its own database rather
    // than truncating a shared one.
    // (OwnerConnectionString, AppConnectionString) for a new database: the
    // owner runs migrations, the app runs as pf_app.
    public static (string Owner, string App) CreateDatabase()
    {
        var container = Instance.Value;
        var name = $"test_{Guid.NewGuid():N}";

        using (var admin = new NpgsqlConnection(container.GetConnectionString()))
        {
            admin.Open();
            using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            create.ExecuteNonQuery();
        }

        var owner = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name };
        var app = new NpgsqlConnectionStringBuilder(owner.ConnectionString) { Username = "pf_app", Password = AppRolePassword };
        return (owner.ConnectionString, app.ConnectionString);
    }
}

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly (string Owner, string App) _database = PostgresContainer.CreateDatabase();

    // Runs migrations; bypasses nothing at runtime because nothing uses it then.
    public string OwnerConnectionString => _database.Owner;

    // What the API itself connects with: pf_app, subject to row-level security.
    public string ConnectionString => _database.App;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["ConnectionStrings:Migrations"] = OwnerConnectionString,
                ["Jwt:Key"] = "super-secret-test-key-that-is-long-enough-32chars",
                ["Jwt:Issuer"] = "PersonalFinance.Api",
                ["Jwt:Audience"] = "PersonalFinance.Api",
                ["Plaid:ClientId"] = "test-client",
                ["Plaid:Secret"] = "test-secret",
                ["Plaid:Environment"] = "Sandbox"
            });
        });
    }
}

public static class SystemScopeExtensions
{
    // A DI scope whose database access runs as system code (DbSystemScope),
    // for tests that seed or inspect rows directly rather than through a
    // signed-in request - row-level security would otherwise hide them (#119).
    public static IServiceScope SystemScope(this Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) =>
        new Scope(factory.Services.CreateScope(), PersonalFinance.Api.Data.DbSystemScope.Enter());

    private sealed class Scope(IServiceScope inner, IDisposable system) : IServiceScope
    {
        public IServiceProvider ServiceProvider => inner.ServiceProvider;

        public void Dispose()
        {
            inner.Dispose();
            system.Dispose();
        }
    }
}
