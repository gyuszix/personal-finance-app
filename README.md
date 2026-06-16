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
