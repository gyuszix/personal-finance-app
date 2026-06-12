# Personal Finance Tracker

A cross-platform personal finance app that connects to real bank accounts and provides spending insights, transaction history, and budget tracking.

## What it does

- Links to bank accounts via Plaid (supports Bank of America and most US banks)
- Fetches and displays real transaction history and account balances
- Categorises spending automatically
- Tracks budgets and financial goals
- Runs natively on iOS, Android, macOS, and Windows from a single codebase

## Tech stack

| Layer | Technology |
|---|---|
| Mobile / Desktop UI | .NET MAUI (C# + XAML) |
| Backend API | ASP.NET Core (Minimal APIs) |
| Authentication | ASP.NET Identity + JWT |
| ORM | Entity Framework Core |
| Database | PostgreSQL |
| Bank data | Plaid API |
| Hosting | Azure App Service |
| Managed DB | Azure Database for PostgreSQL |
| Containers | Docker |

## Architecture

The MAUI frontend communicates exclusively with the ASP.NET Core backend via HTTPS REST. The backend handles all business logic, authentication, and third-party API calls. Plaid credentials and bank data never touch the client directly.

## Design decisions

### Bank data sync (mirroring)

- All Plaid data is **mirrored into Postgres** and served from there — the client never waits on Plaid.
- Sync is **webhook-driven**: Plaid's `SYNC_UPDATES_AVAILABLE` webhook triggers a cursor-based `/transactions/sync` call. No scheduled polling.
- Syncs run in-process via a `BackgroundService` + in-memory channel — no queue infrastructure.
- **6 months** of transaction history backfilled when an account is first linked.
- Balances are **cached from sync** and shown with an "as of" timestamp. The expensive live `/accounts/balance/get` endpoint is never called.
- Sync correctness rules: upsert on `plaid_transaction_id`, replace pending transactions when the posted version arrives (`pending_transaction_id`), honor `removed` transactions.

### Authentication

- ASP.NET Identity, email + password (social login deferred; email verification deferred but `EmailConfirmed` kept for later).
- **15-minute JWT** access tokens + **rotating refresh tokens** (opaque, stored hashed in Postgres, ~30–60 day expiry).
- Refresh token **reuse detection**: a revoked token presented again kills the whole token family and forces re-login.
- Client stores the refresh token in MAUI `SecureStorage` (Keychain / Keystore / DPAPI); access token lives in memory only. A `DelegatingHandler` handles 401 → refresh → retry transparently.
- Plaid access tokens are encrypted app-side (ASP.NET Data Protection) and stored server-side only. The client only ever handles short-lived `link_token` / `public_token` values from Plaid Link.

### Categorization

- Plaid's `personal_finance_category` taxonomy is used directly as the budget category scheme.
- A separate nullable `user_category_override` column wins when set — sync only writes Plaid's columns, so user re-categorization is never overwritten.
- (v2 hook: a `merchant_rules` table for "always categorize merchant X as Y".)

### API

- Minimal APIs grouped by feature, versioned under `/api/v1/`.
- **Pull-only** in v1: no push notifications or SignalR. Client refreshes on app-open / pull-to-refresh.
- Cursor-based pagination on transactions (`?after=<id>&limit=50`).
- Multi-tenancy enforced centrally via an EF Core **global query filter on `UserId`** — every query is tenant-scoped by construction.

### MAUI client

- MVVM with **CommunityToolkit.Mvvm**, Shell navigation with tabs: Dashboard, Transactions, Budgets, Settings.
- **Refit** for the typed API client, with the auth `DelegatingHandler` underneath.
- Light, **disposable** SQLite cache so the app opens instantly with stale data and refreshes in the background. Server is the source of truth; cache problems are fixed by clear-and-refetch, never reconciliation.

### Cost posture (development)

- Plaid **Sandbox** (free) until the app is feature-complete.
- Postgres via **Docker Compose** locally; Azure (or a free-tier managed Postgres) only at deploy time.
- Single App Service process runs both the API and the background sync worker.

## Project structure

```
src/
├── PersonalFinance.Api/          ASP.NET Core backend
│   ├── Endpoints/                Minimal API endpoint groups (auth, plaid, accounts, transactions, budgets)
│   ├── Services/                 Plaid client, JWT/refresh issuance, background sync worker
│   └── Data/                     AppDbContext + EF Core entities
├── PersonalFinance.Shared/       DTOs shared between API and client (request/response contracts)
└── PersonalFinance.App/          .NET MAUI client
    ├── Views/                    XAML pages (Dashboard, Transactions, Budgets, Settings)
    ├── ViewModels/               CommunityToolkit.Mvvm view models
    └── Services/                 Refit API interface, auth handler, secure token store, SQLite cache
```

## Status

Scaffolding only — file stubs mark where everything goes. Next steps:

1. `dotnet new` the actual projects (webapi, maui, classlib) + solution file over the stubs
2. EF Core model + initial migration
3. Auth endpoints (register / login / refresh)
4. Plaid Sandbox link flow + sync worker
5. MAUI shell + dashboard

## Local development

```bash
docker compose up -d        # Postgres on localhost:5432
```
