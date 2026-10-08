using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Endpoints;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Hubs;
using PersonalFinance.Api.Middleware;
using PersonalFinance.Api.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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

// Skip the scheduled sync job in Testing - integration tests spin up a
// fresh, empty in-memory DB each run, so it would just be dead weight
// (0 accounts to sync), and keeping it out avoids any background noise
// during test runs entirely.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<ScheduledPlaidSyncService>();
}

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Seed database & roles ─────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    // In Testing we run on a fresh in-memory SQLite DB, so build the schema here.
    // (Production applies EF Core migrations separately.)
    if (app.Environment.IsEnvironment("Testing"))
    {
        var db = services.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
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

app.MapHub<TransactionsHub>("/hubs/transactions");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Interactive "try it out" API docs UI - open http://localhost:5140/scalar/v1
    app.MapScalarApiReference();
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.Run();

public partial class Program { }