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

> **Already have a native Postgres (e.g. Homebrew `postgresql@16`)?** It
> binds `127.0.0.1:5432`, which beats the container's `0.0.0.0:5432` for
> `Host=localhost` - so the API silently uses the native server while
> `docker compose up` still looks healthy. Check what owns the port with
> `lsof -nP -iTCP:5432 -sTCP:LISTEN` (a `com.docker` process is the
> container; `postgres` is native), and either stop one of them or point
> the connection string at the one you mean. The API logs which server it
> reached on startup (`Connected to database ...`): the container reports
> a `linux` build, Homebrew a `darwin` one.

For realistic test data without seeding anything yourself, see [`docs/PLAID_SANDBOX.md`](docs/PLAID_SANDBOX.md).

### Pointing the app at a different API

The MAUI app defaults to `http://localhost:5140`. To target another API
without rebuilding, set `PERSONALFINANCE_API_URL` before launching:

```bash
PERSONALFINANCE_API_URL="https://api.example.com" dotnet build src/PersonalFinance.App -f net10.0-maccatalyst -t:Run
```

Unset or empty falls back to the local default.

### Running the API anywhere but your own machine

Stored Plaid access tokens are encrypted with ASP.NET Core Data
Protection. By default its keys live in `~/.aspnet/DataProtection-Keys`,
which is fine locally but is lost with a container and isn't shared
between instances - and without the keys, every stored token stops
decrypting and every sync fails until each bank is re-linked. Point it at
a persistent directory every instance can read:

```bash
DataProtection__KeysPath=/var/lib/personalfinance/keys
```

The key files aren't encrypted at rest - protect that directory like the
database. Don't change the app's content root/application name without a
plan either: that also makes existing tokens unreadable.

## Dashboard

The Dashboard is a set of cards in tabs (Spending, Cash Flow, Savings, Debt,
Net Worth), with one month selector for all of them. A tab only appears
once a card belongs to it. Each card loads independently, so a failing
metric shows its own error and the rest of the Dashboard still works.

### How metrics are defined

Every metric uses the same definition of income and spending
(`Spending.CountedInCashflow` in the API): posted transactions only, with
transfers between your own accounts (`TRANSFER_IN`/`TRANSFER_OUT`) and
credit card payments (`LOAN_PAYMENTS_CREDIT_CARD_PAYMENT`) left out. A card
payment isn't new spending, because the purchases it pays for were already
counted on the card. Plaid's sign convention applies: a positive amount is
money out, a negative amount is money in. Income is take-home pay.

| Card | Tab | Definition |
|---|---|---|
| Spending by category | Spending | The month's spending per Plaid category. Donut of the top 5 plus "Other", total in the centre |
| Net cash flow | Cash Flow | Income − spending for the month, over 12 months of income vs spending bars |
| Net worth | Net Worth | Assets (depository, investment) − liabilities (credit, loan), today |

A ratio with nothing to divide by (no income, no spending) shows "—", never
0% or infinity.

### Adding a Dashboard card

1. **Server:** add a class implementing `IInsight<TResult>` in
   `src/PersonalFinance.Api/Services/Insights/`, with a `Key` (the URL
   segment) and `CalculateAsync(InsightMonth, ...)`. Put its response DTO in
   `PersonalFinance.Shared/DTOs/Insights/`. Then add one line to
   `InsightEndpoints`: `insights.MapInsight<MyInsight, MyResponse>();`.
   Sign-in, `month` validation (yyyy-MM, from 2000-01 up to the current
   month), per-user caching and invalidation on sync all come with it.
   Query through `AppDbContext`, whose per-user filter scopes everything to
   the caller. Use `Ratio.Of` for any division.
2. **App:** add a folder under `src/PersonalFinance.App/Insights/Cards/`
   with a ViewModel that derives from `InsightCardViewModel<MyResponse>`
   (set `Key`, `Title`, `Group`, and `Apply` the response) and a
   `ContentView` with `ControlTemplate="{StaticResource InsightCardTemplate}"`,
   which supplies the title, loading, error and empty states. Use theme
   tokens and `ChartPalette` for colours, and `Format` for money, percentages
   and "—".
3. **Register** it in `MauiProgram`, one line:
   `builder.Services.AddInsightCard<MyCard, MyCardView>();`
4. Add tests for the calculator next to `InsightEndpointsTests`.

## Theme

Dark only: black, with torii vermilion and neon blue as the two accents, and
orange and blue for further chart series. Every colour is a named token in
[`Colors.xaml`](src/PersonalFinance.App/Resources/Styles/Colors.xaml). Views
and charts use the keys, never hex values, so changing a colour is a single edit.

| Token | Hex | Used for | Contrast on `Ink` |
|---|---|---|---|
| `Ink` | `#0B0C10` | page background | - |
| `Surface` | `#15171D` | cards | - |
| `SurfaceRaised` | `#1F222A` | inputs, selected tab | - |
| `Hairline` | `#2C303A` | borders, gridlines | - |
| `TextPrimary` | `#EEF0F3` | text | 17.1:1 |
| `TextMuted` | `#8B919C` | secondary text | 6.2:1 |
| `Torii` | `#F2542D` | brand, primary buttons, credit, negative | 5.7:1 |
| `Neon` | `#1F8BFF` | data, secondary buttons, debit | 5.8:1 |
| `NeonGlow` | `#5CC8FF` | highlights, positive | 10.4:1 |
| `Orange` | `#FF9F1C` | third series, warnings | 9.5:1 |
| `Blue` | `#3B5BFF` | fourth series (chart marks only, not text) | 3.8:1 |

Shared styles in [`Styles.xaml`](src/PersonalFinance.App/Resources/Styles/Styles.xaml):
`Headline`, `SubHeadline`, `Eyebrow` (small caps label), `Figure` (a card's
big number), `Caption`, `ErrorText`, `Card` (a `Border`), and `GhostButton` /
`IconButton` next to the default Torii button.

## CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) builds and runs the
test suite (`PersonalFinance.Tests`, which pulls in `Api` and `Shared`) on
every push/PR to `main`. The MAUI `App` project is intentionally excluded -
it needs mobile workloads the runner doesn't have and isn't covered by tests.
The build also fails on any known-vulnerable NuGet package in that graph
(`dotnet list package --vulnerable --include-transitive`); run the same
command locally to check before pushing. NuGet packages are cached between
runs, keyed on the project files.

Plaid credentials are passed into the workflow as env vars sourced from
[repository secrets](https://github.com/gyuszix/personal-finance-app/settings/secrets/actions),
the same way `dotnet user-secrets` works locally:

```bash
gh secret set PLAID_CLIENT_ID
gh secret set PLAID_SECRET
```

(or add/rotate them via the Settings link above). Today's tests don't
actually call Plaid, so this isn't load-bearing yet - it's wiring for
whenever an integration test does, so nothing ever needs hardcoding.

Since this repo is public: secrets are **not** exposed to workflow runs
triggered by `pull_request` from a fork - that's a deliberate GitHub
protection, not a bug, and not something to work around by switching to
`pull_request_target` with a fork checkout (that combination is the classic
way public repos leak secrets to a malicious PR).

## Further docs

- [`CHANGELOG.md`](CHANGELOG.md) — what shipped, by version
- [`API.md`](API.md) — full endpoint reference (request/response shapes, auth flow) for building against this backend without reading its source
- [`docs/PLAID_SANDBOX.md`](docs/PLAID_SANDBOX.md) — Plaid Sandbox test credentials for local/GUI development
- [`docs/MCP_SETUP.md`](docs/MCP_SETUP.md) — the Postgres and GitHub MCP servers configured in [`.mcp.json`](.mcp.json), the read-only database role they rely on, and the version pins needed to install them
