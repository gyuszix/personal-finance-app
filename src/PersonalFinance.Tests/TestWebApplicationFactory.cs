using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
        return container;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    // A new, empty database on the shared container. Test classes run in
    // parallel, one factory each, so each needs its own database rather
    // than truncating a shared one.
    public static string CreateDatabase()
    {
        var container = Instance.Value;
        var name = $"test_{Guid.NewGuid():N}";

        using (var admin = new NpgsqlConnection(container.GetConnectionString()))
        {
            admin.Open();
            using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            create.ExecuteNonQuery();
        }

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public string ConnectionString { get; } = PostgresContainer.CreateDatabase();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Jwt:Key"] = "super-secret-test-key-that-is-long-enough-32chars",
                ["Jwt:Issuer"] = "PersonalFinance.Api",
                ["Jwt:Audience"] = "PersonalFinance.Api"
            });
        });
    }
}
