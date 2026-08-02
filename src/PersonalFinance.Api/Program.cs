using Microsoft.AspNetCore.Identity;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Endpoints;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ── Docs ──────────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddDatabase(builder.Configuration, builder.Environment);
builder.Services.AddIdentityServices();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPlaidIntegration(builder.Configuration);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

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
app.MapAuthEndpoints();
app.MapPlaidEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.MapTransactionEndpoints();

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.Run();

public partial class Program { }