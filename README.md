# Personal Finance Tracker

**Version:** [0.2.0](CHANGELOG.md#020---2026-09-08)

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

- **Sync:** Plaid data is mirrored into Postgres and served from there. Cursor-based transaction sync (`/transactions/sync`), balances refreshed on each sync.
- **Auth:** 15-min JWT access tokens + rotating refresh tokens (reuse detection revokes the whole token family). Plaid access tokens encrypted at rest.
- **Categorization:** Plaid's `personal_finance_category` taxonomy.
- **API:** versioned `/api/v1/` minimal APIs, cursor/offset pagination, per-user isolation via an EF Core global query filter on `UserId`.
- **Client:** MVVM (CommunityToolkit), server is the source of truth.

## Project structure

```
src/
├── PersonalFinance.Api/      ASP.NET Core backend (Endpoints, Services, Data)
├── PersonalFinance.Shared/   DTOs shared between API and client
└── PersonalFinance.App/      .NET MAUI client (Views, ViewModels, Services)
```

## Local development

```bash
docker compose up -d    # Postgres 16 on localhost:5432, db "personalfinance", user "postgres", no password (trust auth, local dev only)
```

Point the API at it via user secrets (run once, from `src/PersonalFinance.Api/`):

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=personalfinance;Username=postgres"
dotnet user-secrets set "Jwt:Key" "<32+ character secret key>"
dotnet user-secrets set "Jwt:Issuer" "PersonalFinance.Api"
dotnet user-secrets set "Jwt:Audience" "PersonalFinance.Api"
dotnet user-secrets set "Plaid:ClientId" "<your Plaid sandbox client id>"
dotnet user-secrets set "Plaid:Secret" "<your Plaid sandbox secret>"
dotnet user-secrets set "Plaid:Environment" "sandbox"
```

Then apply migrations: `dotnet ef database update --project src/PersonalFinance.Api`

For realistic test data without seeding anything yourself, see [`docs/PLAID_SANDBOX.md`](docs/PLAID_SANDBOX.md).

## Further docs

- [`CHANGELOG.md`](CHANGELOG.md) — what shipped, by version
- [`API.md`](API.md) — full endpoint reference (request/response shapes, auth flow) for building against this backend without reading its source
- [`docs/PLAID_SANDBOX.md`](docs/PLAID_SANDBOX.md) — Plaid Sandbox test credentials for local/GUI development
