# Changelog

All notable changes to this project are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
version numbers follow [Semantic Versioning](https://semver.org/). The
version lives in one place - [`Directory.Build.props`](Directory.Build.props)
at the repo root, shared by every project under `src/`. A version bump should
come with a matching git tag (`vX.Y.Z`) and an entry here.

## [Unreleased]

### Added
- Debt-to-income card on a new Debt tab, measured against take-home pay
  from your deposits (no settings needed), with payments broken down by
  loan type. Credit card payments aren't counted as debt service (#89)
- Emergency fund card on the Savings tab: months of usual spending your
  checking and savings cover, against the 3-6 month guideline (#90)
- Burn rate card on the Cash Flow tab: average monthly spending over the
  last three complete months, and how this month is pacing against it
  (#92)
- Savings rate card on the Savings tab: the share of take-home pay kept
  this month, with twelve months of saved (neon) and overspent (torii)
  bars. "—" with no income (#91)
- Credit vs debit cards on the Spending tab: the month's split with the
  average purchase on each, credit's share over 12 months, the split per
  category, and card payoff (payments ÷ credit spending, each payment
  counted once even when both the bank and card ends are linked). Four new
  `/insights` endpoints (#87)
- Dashboard is now cards in tabs with charts (LiveCharts2): spending by
  category as a donut (top 5 + Other), net cash flow with 12 months of
  income vs spending bars, and net worth. Each card loads and fails on
  its own. Server side, each metric is an `IInsight` served at
  `GET /insights/{key}?month=yyyy-MM`, which requires sign-in, checks the
  month is between 2000-01 and the current month, and caches per user
  until the next sync. README explains how to add a card (#85)
- Dark theme: black with torii vermilion and neon blue accents, plus
  orange and blue. Replaces the MAUI template's purple. Every colour is a
  named token in `Colors.xaml`, and the views no longer hardcode any.
  Adds the `Card`, `Eyebrow`, `Figure`, `Caption`, `ErrorText`,
  `GhostButton` and `IconButton` styles. README lists the palette (#86)
- A daily balance snapshot per account (`BalanceSnapshots`), recorded on
  every sync; the last sync of the day wins. The migration seeds today
  from current balances. This is the history the net worth trend reads,
  and it only goes back to the day this was applied. Removed with the
  account when a bank is unlinked (#84)
- Each account's Plaid subtype (Checking, Savings, CreditCard, ...) is
  stored and returned on `AccountResponse.Subtype`, so liquid cash can be
  told apart from CDs and HSAs. Set at link time; accounts linked
  earlier pick it up on their next sync (#83)
- Dashboard can step back through past months of cash flow; the
  Transactions category filter offers every category the user has, not
  just this month's, via the new `GET /transactions/categories` (#63)
- Unlink a bank from the Accounts tab (`DELETE /accounts/{id}`): removes
  the Plaid Item and every account it brought in, with their
  transactions (#64)
- App refreshes itself when the API pushes `SyncCompleted` over SignalR,
  so the scheduled background sync shows up without tapping Refresh (#62)
- `DataProtection:KeysPath` setting for where the keys encrypting stored
  Plaid tokens live - required anywhere but a single dev machine (#57)
- API logs which Postgres server it connected to on startup, so a native
  Postgres silently shadowing the docker-compose one is visible (#42)
- Integration tests for the transaction, account and sync endpoints,
  per-user isolation, and the SignalR hub (#60)
- CI fails on known-vulnerable NuGet packages and caches restores (#61)
- Accounts tab lists the user's linked accounts (bank name, account type,
  balance) instead of a "Coming soon" placeholder, read from the
  per-account breakdown `/accounts/summary` was already returning (#39)
- Explicit Refresh button on Dashboard, Transactions and Accounts. Nothing
  in the app could be reloaded without navigating away and back, so a
  transient failure was a dead end and newly synced data never appeared.
  macOS is the first target platform and pull-to-refresh isn't a desktop
  gesture, hence a visible button rather than a `RefreshView` (#45)
- Transaction rows show date and category alongside description and
  amount - both were already on `TransactionResponse` and returned by the
  API, just never bound (#46)
- Empty/first-run states on Dashboard, Transactions and Accounts. A user
  with nothing linked previously saw a wall of `$0.00` (indistinguishable
  from a failed load) and a blank transaction list, with nothing pointing
  at "Connect a bank" (#47, #39)
- Transactions page displays `ErrorMessage`, which the ViewModel had been
  setting all along with no Label bound to it - errors on that page were
  silently invisible (#48)

- CI workflow (`.github/workflows/ci.yml`) building/testing the API on
  push/PR to `main`, with Plaid sandbox credentials wired in from GitHub
  Actions repo secrets instead of ever being hardcoded
- MAUI app persists the access/refresh token pair in `SecureStorage` and
  restores the session on launch instead of always starting at login;
  a 401 now triggers a silent refresh-and-retry instead of logging the
  user out
- Real "Connect a bank" flow - `PlaidLinkWebService` hosts Plaid's Link
  web SDK in a `WebView` and replaces the old action-sheet stub, so
  linking a bank now works from the app itself on every platform
- Login hung indefinitely on Mac Catalyst - `SecureStorage` (Keychain)
  access failed under App Sandbox without a `keychain-access-groups`
  entitlement, throwing mid-login before the loading spinner ever
  cleared. Sandbox is off for local dev until proper entitlements/signing
  are set up, and session persistence now degrades gracefully instead of
  taking the whole login down if Keychain access ever fails again

### Changed
- `/accounts/summary` no longer logs the user's net worth - logs carry
  IDs and counts only (#90)
- `ApiConfig.BaseUrl` reads from the `PERSONALFINANCE_API_URL` environment
  variable, falling back to `http://localhost:5140`. Was a `const`, so the
  endpoint couldn't change without a rebuild and the value was inlined into
  every consuming assembly at compile time (#50)
- Dashboard's load-failure message no longer tells the user to "pull to
  refresh" - the app has never implemented that gesture (#45)

### Changed
- MAUI networking layer switched from hand-rolled `HttpClient` calls to
  a Refit-generated `IPersonalFinanceApi` client. Token attachment and
  refresh-on-401 now live in one `AuthRefreshHandler` instead of being
  repeated per method; `ApiService` is a thin façade over the typed
  client so ViewModels didn't need to change

### Changed
- Registering now signs the user straight in instead of sending them
  back to the login page to re-type the same credentials
- Login page has a Register button - the register route existed but
  was only reachable by navigating to it directly, nothing in the UI
  linked to it

### Fixed
- Cash flow no longer counts credit card payments: paying the card from
  checking was counted as spending on top of the card purchases it paid
  for, and the payment landing on the card as income. Which transactions
  count as income and spending is now defined once, in `Spending` (#82)
- Transactions duplicated by the pre-#41 sync are removed: one row is
  kept per Plaid transaction, sync cursors are reset so the next sync
  re-attributes each one to its real account, and a unique index on
  `PlaidTransactionId` stops duplicates coming back. The migration
  deletes rows and its Down can't restore them - back up the database
  before applying it anywhere but local dev. Credit card spending now
  appears on the credit accounts (#53)
- A Plaid error from `/transactions/sync` (e.g. an Item needing
  re-login) is now reported with Plaid's error code instead of
  crashing the sync with a null reference (#53)
- Plaid sync ran once per account instead of once per Item, storing
  every transaction once per account in its Item and inflating every
  total (#41)
- Bank names show the institution's name instead of Plaid's raw
  institution ID; existing accounts are fixed on their next sync (#40)
- Two requests hitting an expired token at once both refreshed it,
  tripping reuse detection and logging the user out (#54)
- One failing bank connection no longer aborts a manual sync for the
  others; the response lists `failedAccountIds` (#59)
- Deleting a transaction now invalidates the cached summaries (#58)
- Transactions tab fetched page 1 twice on every load. Rebuilding the
  category list replaced the `Categories` collection, which reset the
  bound Picker's selection and fired the filter-changed handler - so a
  fire-and-forget page-1 load raced the one `LoadTransactionsAsync` was
  already about to start, with both writing `Transactions`, `IsLoading`
  and `_currentPage`. The rebuild no longer counts as a filter change,
  and the user's active filter survives a refresh instead of being
  silently reset (#51)
- Losing connectivity mid-scroll on Transactions stranded `IsLoadingMore`
  as `true`. Because the method's own guard checks that flag, paging was
  then blocked for the rest of the session and the footer spinner spun
  forever. The call is now wrapped in try/catch with the flag reset in a
  `finally`, and the failure is surfaced to the user (#49)
- App crashed on every launch right after the Refit switch -
  `AddRefitClient` defaults to a reflection-based request builder that
  isn't installed/AOT-safe on Mac Catalyst, throwing
  `NotSupportedException` the moment the typed client was first
  resolved. Switched to `AddRefitGeneratedClient`, which uses the
  compile-time source-generated implementation instead

### Fixed
- Dashboard/Transactions tabs never loaded data in the running MAUI app -
  the `Appearing`-bound `EventToCommandBehavior` never fired inside a Shell
  `TabBar`. Replaced with an `OnAppearing()` override that calls the load
  command directly, and added global `AppDomain`/`TaskScheduler` unhandled
  exception logging, since this failure mode produced zero console output
  and no error banner
- `GET /plaid/link-token` returned `link_token` (snake_case) while the MAUI
  client deserialized into `LinkToken` (camelCase) - the field never bound,
  silently breaking "Connect a bank" with "Couldn't start bank connection.
  Please try again."
- `POST /plaid/exchange-token` crashed with an unhandled
  `ArgumentNullException` whenever Plaid rejected the public token (e.g. a
  stub/simulated token); now returns `400 Bad Request` instead
- "Connect a bank" now triggers `POST /transactions/sync` immediately after
  linking, instead of relying on the up-to-30-minute background sync job to
  pick up newly linked accounts
- Re-linking an already-connected Item created duplicate `Account` rows -
  `/plaid/exchange-token` now upserts by `PlaidAccountId`, backed by a
  unique index (migration also cleans up any duplicates already sitting in
  a dev database)

### Security
- Offline transaction cache is cleared on logout and sign-in - it
  previously leaked one user's transactions to the next on the same
  device (#55)
- Per-user query filters fail closed: with no signed-in user they match
  nothing instead of everything (#56)
- App no longer ships SQLitePCLRaw 2.1.11's vulnerable native SQLite
  (GHSA-2m69-gcr7-jv3q) (#66)

## [0.2.0] - 2026-09-08

Backend hardening/scaling pass - the four items on the "someday" backlog,
all built with framework-native tooling, no new paid services.

### Added
- Rate limiting (`Microsoft.AspNetCore.RateLimiting`) - stricter policy on
  `/auth/register`/`/auth/login` (10 req/min/IP), general policy elsewhere
  (200 req/min/IP)
- Response caching (`IMemoryCache`) for `accounts/summary`,
  `transactions/summary`, and `transactions/cashflow`, invalidated on sync
- `ScheduledPlaidSyncService` - background job syncing every linked account
  across every user on a timer (`Sync:IntervalMinutes`, default 30),
  instead of only syncing when a client calls `POST /transactions/sync`
- Real-time sync notifications via SignalR (`TransactionsHub`) - connected
  clients get pushed a `SyncCompleted` event instead of having to poll
- Interactive API docs (`Scalar.AspNetCore`) at `/scalar/v1` in Development

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
