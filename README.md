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

## [4] EF Core Global Query Filter + Resource-Based Authorization

### What was done
- Added a global query filter on `AppDbContext` scoping `Accounts` and `Transactions` to the current user automatically
- Injected `IHttpContextAccessor` into `AppDbContext` to read the current user's ID from JWT claims at query time
- Added `ResourceOwnerRequirement` and `TransactionOwnerHandler` implementing `IAuthorizationHandler` for resource-based authorization
- Wired the new authorization policy into `Program.cs`
- Used `.IgnoreQueryFilters()` in `DELETE /transactions/{id}` so a not-owned transaction can still be found (returning 403 Forbidden) instead of silently 404ing

### New files

```
src/PersonalFinance.Api/
└── Authorization/
    ├── ResourceOwnerRequirement.cs
    └── TransactionOwnerHandler.cs
```

### Why both a query filter AND resource-based auth?

They solve different problems:
- **Query filter** — prevents a user from ever *seeing* another user's rows in list/query results (defense at the data-access layer)
- **Resource-based auth** — for single-resource operations (like `DELETE /transactions/{id}`), gives an explicit 403 Forbidden rather than a 404, and is the reusable pattern for "does this user own this specific resource?"

### Notes
- `IgnoreQueryFilters()` is needed specifically in the delete flow — without it, EF Core's global filter would silently exclude another user's transaction from the query entirely, returning 404 instead of the more correct 403
- This pattern (fetch with `IgnoreQueryFilters`, then explicitly authorize) generalizes to any per-resource ownership check

---

## [5] Refresh Tokens + Rotation + Reuse Detection

### What was done
- Added `RefreshToken` entity: `UserId`, `Token`, `ExpiresAt`, `IsRevoked`, `ReplacedByToken`
- Added `RefreshTokens` DbSet to `AppDbContext`
- Updated `/auth/login` to issue a refresh token alongside the JWT
- Added `/auth/refresh` endpoint with token rotation — old token is revoked, a new one issued
- Added `/auth/revoke` endpoint for explicit logout
- Reuse detection: if a revoked token is presented again (replay attack), the entire token family for that user is nuked
- Migration: `AddRefreshtokens`

### Why rotation + reuse detection?

Short-lived JWTs (1hr) limit exposure if stolen, but requiring re-login every hour is bad UX. Refresh tokens let a client silently get a new JWT — but a refresh token is powerful (it can mint new access tokens), so:
- **Rotation** — every refresh issues a brand new refresh token and revokes the old one, so a refresh token is single-use
- **Reuse detection** — if someone tries to reuse an already-revoked refresh token, that's a strong signal the token was stolen and used by an attacker (racing the legitimate user). Nuking the whole family forces a fresh login, containing the damage.

### Notes
- Refresh tokens are stored as opaque random strings (`RandomNumberGenerator.GetBytes(64)`, base64-encoded), not JWTs — no need for them to be self-describing
- 7-day expiry on refresh tokens vs 1-hour on access tokens

---

## [6] Integration Tests

### What was done
- Added `PersonalFinance.Tests` project with xUnit and `WebApplicationFactory`
- Added `TestWebApplicationFactory` running in a `Testing` environment against an in-memory SQLite DB
- Used a shared-cache SQLite connection so schema persists across requests within a test run
- Injected JWT config via in-memory configuration for the test environment
- Guarded role seeding in `Program.cs` behind an environment check (so seeding doesn't clash with test isolation)
- Added `EnsureCreated()` for the `Testing` environment to build schema at startup (skips full EF Core migrations for speed)
- Tests cover: register, login success, login wrong password, token refresh, reuse detection

### New files

```
src/PersonalFinance.Tests/
├── PersonalFinance.Tests.csproj
├── AuthEndpointsTests.cs
└── UnitTest1.cs
```

### Why `WebApplicationFactory` + SQLite in-memory instead of hitting real Postgres?

- **Speed** — in-memory SQLite spins up instantly per test run, no external DB dependency
- **Isolation** — each test run gets a clean schema, no risk of polluting the real dev database
- **Still an integration test** — `WebApplicationFactory` boots the actual `Program.cs` pipeline (middleware, DI, endpoints), so this is testing real request/response behavior, not just isolated unit logic

### Notes
- `apsettings.Testing.json` (note: contains original typo) supplies test-specific config
- Reuse detection test is the most valuable one here — it actually exercises the "steal and replay a refresh token" attack path and confirms the whole token family gets revoked

---

## [7] Structured Logging + Global Exception Handling

### What was done
- Added `ILogger<Program>` to `GET /transactions` and `DELETE /transactions/{id}`, logging user context and outcomes at key decision points (no linked accounts, sync counts, returned counts, not-found/forbidden/deleted)
- Added `GlobalExceptionHandler : IExceptionHandler` in `Middleware/GlobalExceptionHandler.cs`
- Registered via `AddExceptionHandler<GlobalExceptionHandler>()` and `AddProblemDetails()` in `Program.cs`
- Critically: `app.UseExceptionHandler()` must run **first** in the middleware pipeline — middleware wraps everything registered after it, so it needs to be first to catch exceptions from auth, endpoints, etc.

### New files

```
src/PersonalFinance.Api/
└── Middleware/
    └── GlobalExceptionHandler.cs
```

### Why this matters

- **Before:** unhandled exceptions returned raw stack traces to the client (dev-mode default) — leaking file paths, SQL, internal class names
- **After:** client gets a clean, RFC 7807-compliant `ProblemDetails` JSON response (`{"type", "title", "status"}`), while the full exception (via `_logger.LogError(exception, ...)`) is captured server-side for debugging

### Notes
- Verified by temporarily renaming a Postgres table to force a real DB exception — confirmed client got a generic 500 `ProblemDetails` response while the server log captured the full stack trace and SQL error
- `ILogger` calls use structured placeholders (`"User {UserId} has no linked accounts"`, args passed separately) rather than string interpolation, so log fields stay queryable rather than being baked into a single string

---

## [8] Request Validation + Secrets Management

### What was done
- Added data annotations (`[Required]`, `[EmailAddress]`, `[MinLength(8)]`) to `RegisterRequest`
- Added `ValidationFilter<T> : IEndpointFilter` in `Middleware/ValidationFilter.cs`, using `System.ComponentModel.DataAnnotations.Validator` to validate any decorated DTO
- Attached the filter to `POST /auth/register` via `.AddEndpointFilter<ValidationFilter<RegisterRequest>>()`
- Initialized `dotnet user-secrets` for `PersonalFinance.Api`
- Moved `ConnectionStrings:Default`, `Jwt:Key`/`Issuer`/`Audience`, and `Plaid:ClientId`/`Secrets`/`Environment` out of `appsettings.Development.json` into user secrets
- Emptied `appsettings.Development.json` to `{}` — no sensitive values committed to source control

### Why a reusable endpoint filter instead of manual checks?

Minimal APIs don't get automatic model validation the way MVC controllers with `[ApiController]` do. `IEndpointFilter` runs before the handler and can short-circuit the pipeline — so one `ValidationFilter<T>` class is reusable across any endpoint that takes a validated DTO, returning a standard `ValidationProblem` (per-field errors, RFC 9110 shape) before ever touching `UserManager` or the database.

### Where do user secrets actually live?

`~/.microsoft/usersecrets/{UserSecretsId}/secrets.json` — outside the repo entirely, tied to the local machine and user account via a `UserSecretsId` GUID in the `.csproj` (that GUID itself is safe to commit). **Secrets do not travel with the repo** — cloning onto a new machine requires re-running `dotnet user-secrets set` for each value, since the secrets file itself is never part of git history.

### Notes
- Verified via curl: missing password, invalid email format, and under-length password all short-circuit with structured 400 responses, no DB hit
- Verified secrets precedence: emptied `appsettings.Development.json` to `{}` (an empty file with zero bytes is *not* valid JSON and breaks startup — must be `{}`) and confirmed the app still ran correctly, proving all config now comes from user secrets alone

---

## Status: Core Backend Learning Priorities — Complete

All originally-scoped core backend items are done: EF Core global query filter, resource-based authorization, refresh token rotation + reuse detection, integration tests, structured logging, global exception handling, request validation, and secrets management.

### Remaining (secondary / lower priority — Plaid & MAUI specific)
- Plaid cursor sync (`/transactions/sync`) — replace 30-day polling with proper incremental sync
- Encrypted Plaid access token storage (`IDataProtector`)
- Pagination on `GET /transactions`
- `BankName` / `AccountType` / `personal_finance_category` from Plaid metadata
- API versioning (`/v1/...`)
- Refit client for MAUI (replacing plain `HttpClient`)
- Local SQLite cache in MAUI (offline support)
- `docker-compose.yml` for reproducible local Postgres setup