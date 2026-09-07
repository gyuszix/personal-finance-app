# Changelog

All notable changes to this project are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
version numbers follow [Semantic Versioning](https://semver.org/). The
version lives in one place - [`Directory.Build.props`](Directory.Build.props)
at the repo root, shared by every project under `src/`. A version bump should
come with a matching git tag (`vX.Y.Z`) and an entry here.

## [Unreleased]

## [0.1.0] - 2026-09-07

Initial backend implementation - everything from project scaffolding through
the first full pass of security/reliability hardening and the ratios-page
backend surface.

### Added
- ASP.NET Core Minimal API backend, PostgreSQL via EF Core, MAUI client
  (login/register/transactions screens, MVVM)
- ASP.NET Identity + JWT auth (15-min access tokens), role-based
  authorization (Admin/User)
- Rotating refresh tokens with reuse detection (a replayed/revoked token
  revokes the whole token family)
- EF Core global query filter scoping `Accounts`/`Transactions` to the
  authenticated user, plus resource-based authorization for single-resource
  operations (e.g. delete)
- DTO layer (`PersonalFinance.Shared`) separating API responses from EF
  Core entities, so internal/Plaid identifiers never reach the client
- Plaid integration: Link token creation, public/access token exchange,
  cursor-based transaction sync (`/transactions/sync`) replacing 30-day
  polling, `personal_finance_category` categorization
- Plaid access tokens encrypted at rest (`IDataProtector`)
- Account balance refresh folded into the sync flow (previously set once
  at link time and never updated again)
- API versioning (`/api/v1/...`) via `Asp.Versioning.Http`
- Pagination and category filtering on `GET /transactions`
- `GET /transactions/summary` - spend by category for a given month
- `GET /transactions/cashflow` - income vs. expense for a given month
  (excludes internal transfers)
- `GET /accounts/summary` - total balance, assets/liabilities split, net
  worth, per-account breakdown
- Global exception handling (RFC 7807 `ProblemDetails`), structured logging
- Request validation via a reusable `IEndpointFilter`
- Secrets moved to `dotnet user-secrets` (nothing sensitive committed)
- `docker-compose.yml` for a reproducible local Postgres
- Integration test suite (`WebApplicationFactory` + xUnit + in-memory SQLite)

### Fixed
- `Microsoft.OpenApi` and `SQLitePCLRaw.lib.e_sqlite3` high-severity
  package vulnerabilities
- `[AsParameters]`-bound query parameters were treating non-nullable value
  types as required, ignoring their C# default values
