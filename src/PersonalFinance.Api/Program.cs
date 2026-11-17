using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Endpoints;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Hubs;
using PersonalFinance.Api.Middleware;
using PersonalFinance.Api.Observability;
using PersonalFinance.Api.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Observability ─────────────────────────────────────────────────────────────
builder.AddObservability();

// ── Docs ──────────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddDatabase(builder.Configuration, builder.Environment);
builder.Services.AddIdentityServices();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPlaidIntegration(builder.Configuration);
builder.Services.AddApiVersioningSetup();
builder.Services.AddRateLimitingSetup(builder.Environment);
builder.Services.AddSummaryCaching();
builder.Services.AddRealtimeUpdates();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecksSetup();
builder.Services.AddHttpsHardening(builder.Configuration, builder.Environment);

// Skip the scheduled sync job in Testing - integration tests spin up a
// fresh, empty database per test class, so it would just be dead weight
// (0 accounts to sync), and keeping it out avoids any background noise
// during test runs entirely.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<ScheduledPlaidSyncService>();
}

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── One-off commands ──────────────────────────────────────────────────────────
// `migrate`: applies EF migrations as the schema owner
// (ConnectionStrings:Migrations), then - if Database:AppRolePassword is set -
// lets pf_app sign in with it. What the compose `migrate` service and the
// deploy job run, so production needs no EF tooling (#122).
if (args.FirstOrDefault() == "migrate")
{
    var ownerConnection = app.Configuration.GetConnectionString("Migrations")
        ?? throw new InvalidOperationException("migrate needs ConnectionStrings:Migrations (the schema owner).");
    var ownerOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ownerConnection).Options;
    await using var owner = new AppDbContext(ownerOptions, new HttpContextAccessor());

    var pending = (await owner.Database.GetPendingMigrationsAsync()).ToList();
    app.Logger.LogInformation("Applying {Count} migrations: {Migrations}", pending.Count, pending);
    await owner.Database.MigrateAsync();

    var appRolePassword = app.Configuration["Database:AppRolePassword"];
    if (!string.IsNullOrEmpty(appRolePassword))
    {
        // Can't be a bind parameter in DDL; quote it as a literal instead.
        var literal = "'" + appRolePassword.Replace("'", "''") + "'";
        await owner.Database.ExecuteSqlRawAsync($"ALTER ROLE pf_app LOGIN PASSWORD {literal}");
        app.Logger.LogInformation("pf_app can sign in");
    }
    return 0;
}

// `dotnet run --project src/PersonalFinance.Api -- rewrap-secrets`: key
// rotation, moving every stored secret onto the current KEK (#120).
if (args.FirstOrDefault() == "rewrap-secrets")
{
    using var scope = app.Services.CreateScope();
    var report = await scope.ServiceProvider.GetRequiredService<PersonalFinance.Api.Services.Encryption.SecretRewrapper>().RewrapAllAsync();
    Console.WriteLine($"Plaid tokens re-wrapped: {report.PlaidTokensRewrapped}");
    Console.WriteLine($"Identity tokens re-wrapped: {report.IdentityTokensRewrapped}");
    Console.WriteLine($"Failed: {report.Failed}");
    Console.WriteLine("Secrets by key now: " + string.Join(", ", report.RemainingByKeyId.Select(kv => $"{kv.Key}={kv.Value}")));
    return report.Failed == 0 ? 0 : 1;
}

// ── Seed database & roles ─────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    // Tests get a fresh, empty Postgres database per factory (#111), so build
    // it from the real migrations - a broken migration fails the suite.
    // (Everywhere else migrations are applied separately, never on startup.)
    if (app.Environment.IsEnvironment("Testing"))
    {
        // As the owner (ConnectionStrings:Migrations): the app's own role
        // can't change the schema (#119).
        var ownerOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(app.Configuration.GetConnectionString("Migrations") ?? app.Configuration.GetConnectionString("Default"))
            .Options;
        await using var owner = new AppDbContext(ownerOptions, new HttpContextAccessor());
        await owner.Database.MigrateAsync();
    }
    else
    {
        // Say which Postgres we actually reached. A native Postgres bound to
        // 127.0.0.1:5432 silently wins over the docker-compose one on
        // 0.0.0.0:5432 (#42); version() names the build platform, so a
        // "linux" server is the container and a "darwin" one is Homebrew.
        var db = services.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version()";
        var serverVersion = await command.ExecuteScalarAsync();
        app.Logger.LogInformation(
            "Connected to database {Database} on {DataSource}: {ServerVersion}",
            connection.Database, connection.DataSource, serverVersion);
        await connection.CloseAsync();
    }

    // Row-level security only binds a role that isn't superuser, BYPASSRLS
    // or the tables' owner (#119).
    await DatabaseRoleCheck.EnforceAsync(
        services.GetRequiredService<AppDbContext>(), app.Environment, app.Logger);

    // Seed roles in every environment — registration assigns the "User" role,
    // so it must exist before any user can register.
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    string[] roles = ["Admin", "User"];

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
}

// ── Endpoints ─────────────────────────────────────────────────────────────────
var apiVersionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1.0))
    .ReportApiVersions()
    .Build();

var apiV1 = app.MapGroup("/api/v{version:apiVersion}")
    .WithApiVersionSet(apiVersionSet);

apiV1.MapAuthEndpoints();
apiV1.MapPlaidEndpoints();
apiV1.MapTransactionEndpoints();
apiV1.MapAccountEndpoints();
apiV1.MapInsightEndpoints();
apiV1.MapBudgetEndpoints();
apiV1.MapAuditEndpoints();
apiV1.MapMfaEndpoints();
apiV1.MapCategoryRuleEndpoints();

app.MapHub<TransactionsHub>("/hubs/transactions");
app.MapHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Interactive "try it out" API docs UI - open http://localhost:5140/scalar/v1
    app.MapScalarApiReference();
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Forwarded headers first, so everything after sees the real scheme and client.
app.UseHttpsHardening();
app.UseRequestLogging();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.Run();
return 0;

public partial class Program { }