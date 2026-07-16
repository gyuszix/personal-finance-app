# Personal Finance Tracker

A cross-platform personal finance app that connects to bank accounts (via Plaid) and provides spending insights, transaction history, and budget tracking. Runs on iOS, Android, macOS, and Windows from a single codebase.

## Tech stack

| Layer | Technology |
|---|---|
| UI | .NET MAUI (C# + XAML) |
| Backend | ASP.NET Core (Minimal APIs) |
| Auth | ASP.NET Identity + JWT |
| ORM / DB | Entity Framework Core + PostgreSQL |
| Bank data | Plaid API |

## Architecture

MAUI client talks only to the ASP.NET Core backend over HTTPS REST. The backend owns all business logic, auth, and Plaid calls — bank data and Plaid credentials never touch the client.

- **Sync:** Plaid data is mirrored into Postgres and served from there. Webhook-driven cursor sync, 1 year of history backfilled on first link, cached balances only.
- **Auth:** 15-min JWT access tokens + rotating refresh tokens (stored hashed, with reuse detection). Plaid tokens encrypted server-side.
- **Categorization:** Plaid's `personal_finance_category` taxonomy, overridable per-transaction by the user.
- **API:** versioned `/api/v1/` minimal APIs, cursor pagination, per-user isolation via an EF Core global query filter on `UserId`.
- **Client:** MVVM (CommunityToolkit), Refit API client, disposable SQLite cache (server is source of truth).

## Project structure

```
src/
├── PersonalFinance.Api/      ASP.NET Core backend (Endpoints, Services, Data)
├── PersonalFinance.Shared/   DTOs shared between API and client
└── PersonalFinance.App/      .NET MAUI client (Views, ViewModels, Services)
```

## Local development

```bash
docker compose up -d    # Postgres on localhost:5432
```

# Build Log

## [1] Solution Structure

### What was done
- Created a .NET solution as the workspace container
- Created three projects inside `src/`
- Wired all three projects into the solution

### Commands

```bash
# Create solution
dotnet new sln -n PersonalFinance

# Create projects
dotnet new webapi -n PersonalFinance.Api -o src/PersonalFinance.Api
dotnet new classlib -n PersonalFinance.Shared -o src/PersonalFinance.Shared
dotnet new maui -n PersonalFinance.App -o src/PersonalFinance.App

# Wire projects into solution
dotnet sln add src/PersonalFinance.Api/PersonalFinance.Api.csproj
dotnet sln add src/PersonalFinance.Shared/PersonalFinance.Shared.csproj
dotnet sln add src/PersonalFinance.App/PersonalFinance.App.csproj
```

### Structure

```
PersonalFinance.slnx              ← solution (workspace)
src/
├── PersonalFinance.Api/
│   ├── Program.cs                ← backend entry point
│   └── appsettings.json          ← config (db, jwt secret etc)
├── PersonalFinance.Shared/
│   └── Class1.cs                 ← placeholder, DTOs go here
└── PersonalFinance.App/
    ├── MauiProgram.cs            ← frontend entry point
    └── MainPage.xaml             ← first screen
```

### Notes
- `obj/`, `Platforms/`, `Properties/` are generated scaffolding — don't touch
- MAUI workload had to be installed manually: `dotnet workload install maui`
- `Microsoft.OpenApi` vulnerability warning on Api project — non-critical, fix later

---

## [2] Data Layer

### What was done
- Created three entities: `User`, `Account`, `Transaction`
- Created `AppDbContext` wiring entities to the database
- Added PostgreSQL connection string to `appsettings.json`
- Registered DbContext in `Program.cs`
- Ran `InitialCreate` migration — created `Users`, `Accounts`, `Transactions` tables in Postgres

### Files created

```
src/PersonalFinance.Api/
├── Data/
│   └── AppDbContext.cs
├── Entities/
│   ├── User.cs
│   ├── Account.cs
│   └── Transaction.cs
```

### Entities

```csharp
// User.cs
namespace PersonalFinance.Api.Entities;

public class User
{
    public int UserId { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
}

// Account.cs
namespace PersonalFinance.Api.Entities;

public class Account
{
    public int AccountId { get; set; }
    public int UserId { get; set; }
    public decimal Balance { get; set; }
    public string BankName { get; set; }
    public string AccountType { get; set; }
    public string PlaidAccountId { get; set; }
}

// Transaction.cs
namespace PersonalFinance.Api.Entities;

public class Transaction
{
    public int TransactionId { get; set; }
    public int AccountId { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; }
    public DateTime Date { get; set; }
    public string PlaidTransactionId { get; set; }
}
```

### AppDbContext.cs

```csharp
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Account> Accounts { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
}
```

### appsettings.json

```json
"ConnectionStrings": {
    "Default": "Host=localhost;Database=personalfinance;Username=gyuszix"
}
```

### Program.cs additions

```csharp
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
```

### Migration

```bash
dotnet ef migrations add InitialCreate --project src/PersonalFinance.Api
dotnet ef database update --project src/PersonalFinance.Api
```

---

## [3a] Identity Setup

### What was done
- Installed `Microsoft.AspNetCore.Identity.EntityFrameworkCore` NuGet package
- Updated `User.cs` to extend `IdentityUser` — drops manual `UserId`, `Email`, `PasswordHash` as Identity provides these
- Updated `AppDbContext.cs` to extend `IdentityDbContext<User>` — drops `Users` DbSet as Identity manages that table
- Ran `AddIdentity` migration — Identity created its own tables in Postgres
- Wired Identity into `Program.cs` — built-in register and login endpoints now available

### Updated files

```csharp
// User.cs
using Microsoft.AspNetCore.Identity;

namespace PersonalFinance.Api.Entities;

public class User : IdentityUser
{
}
```

```csharp
// AppDbContext.cs
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Data;

public class AppDbContext : IdentityDbContext<User>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Account> Accounts { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
}
```

```csharp
// Program.cs
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using Microsoft.AspNetCore.Identity;
using PersonalFinance.Api.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Registers AppDbContext with DI, tells it to use Postgres with our connection string
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Registers Identity using our User class, stores data in AppDbContext
builder.Services.AddIdentityApiEndpoints<User>().AddEntityFrameworkStores<AppDbContext>();

var app = builder.Build();

// Maps built-in Identity endpoints (register, login etc) to our User class
app.MapIdentityApi<User>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
```

### Identity tables added to Postgres

```
AspNetUsers         ← users (replaces our old Users table)
AspNetRoles         ← roles (e.g. admin, user)
AspNetUserRoles     ← which users have which roles
AspNetUserClaims    ← extra user metadata
AspNetUserLogins    ← external login providers
AspNetUserTokens    ← tokens (e.g. password reset)
AspNetRoleClaims    ← claims attached to roles
```

### Migration

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add AddIdentity
dotnet ef database update
```

### Notes
- `dotnet-ef` CLI tool must be installed separately — not bundled with .NET SDK
- After install, add to PATH: `export PATH="$PATH:/Users/gyuszix/.dotnet/tools"`
- The initial `fail` log on first `database update` is normal — EF checks for the migrations history table, doesn't find it, creates it, then carries on

---

## [3b] JWT Authentication

### What was done
- Installed `Microsoft.AspNetCore.Authentication.JwtBearer` and `System.IdentityModel.Tokens.Jwt` NuGet packages
- Added JWT config to `appsettings.json`
- Wired JWT validation into `Program.cs`
- Created `DTOs/RegisterRequest.cs` and `DTOs/LoginRequest.cs`
- Created `Endpoints/AuthEndpoints.cs` with `/auth/register` and `/auth/login` endpoints
- Tested register and login with curl — login returns a signed JWT

### New files

```
src/PersonalFinance.Api/
├── DTOs/
│   ├── RegisterRequest.cs
│   └── LoginRequest.cs
└── Endpoints/
    └── AuthEndpoints.cs
```

### appsettings.json additions

```json
"Jwt": {
    "Key": "<32+ character secret key>",
    "Issuer": "PersonalFinance.Api",
    "Audience": "PersonalFinance.Api"
}
```

### DTOs

```csharp
// RegisterRequest.cs / LoginRequest.cs
namespace PersonalFinance.Api.DTOs;

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
```

### AuthEndpoints.cs

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using PersonalFinance.Api.DTOs;
using PersonalFinance.Api.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PersonalFinance.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/register", async (RegisterRequest request, UserManager<User> userManager) =>
        {
            var user = new User { UserName = request.Email, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
                return Results.BadRequest(result.Errors);

            return Results.Ok("User registered successfully");
        });

        app.MapPost("/auth/login", async (LoginRequest request, UserManager<User> userManager, IConfiguration config) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
                return Results.Unauthorized();

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds
            );

            return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
        });
    }
}
```

### Final Program.cs

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// ── Docs ──────────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ── Identity ──────────────────────────────────────────────────────────────────
builder.Services.AddIdentityApiEndpoints<User>()
    .AddEntityFrameworkStores<AppDbContext>();

// ── Authentication & Authorisation ────────────────────────────────────────────
builder.Services.AddAuthentication().AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
    };
});

builder.Services.AddAuthorization();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapIdentityApi<User>();
app.MapAuthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.Run();
```

### Notes
- JWT key must be 32+ characters (256 bits) for HS256
- `Issuer` and `Audience` are baked into the token — server validates these on every request
- Token expires after 1 hour — can be adjusted in `AuthEndpoints.cs`
- Tested via curl — register returns `"User registered successfully"`, login returns a signed JWT
- Paste token into jwt.io to inspect header, payload, and signature