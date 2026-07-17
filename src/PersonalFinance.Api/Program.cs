using Microsoft.AspNetCore.Identity;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Endpoints;
using PersonalFinance.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ── Docs ──────────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddIdentityServices();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddPlaidIntegration(builder.Configuration);

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Seed Roles ────────────────────────────────────────────────────────────────
using var scope = app.Services.CreateScope();
var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

string[] roles = ["Admin", "User"];

foreach (var role in roles)
{
    if (!await roleManager.RoleExistsAsync(role))
    {
        await roleManager.CreateAsync(new IdentityRole(role));
    }
}

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapIdentityApi<User>();
app.MapAuthEndpoints();
app.MapPlaidEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.Run();