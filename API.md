# API Reference

Backend base URL in local dev: `http://localhost:5140` (see `Properties/launchSettings.json` in `PersonalFinance.Api` if that changes). All routes are versioned under `/api/v1/`.

This document describes the API surface as it exists today, for building a client against it without reading the backend source. See [`CHANGELOG.md`](CHANGELOG.md) for what changed when.

## Auth

Everything under `/api/v1/transactions`, `/api/v1/accounts`, and `/api/v1/plaid` requires a JWT access token:

```
Authorization: Bearer <token>
```

Access tokens expire after **10 minutes** (`expiresIn` in the login/refresh response, in seconds) and can't be revoked. That's why they're short. Use the refresh token to get a new pair without asking the user to log in again.

Refresh tokens are single-use. Every refresh spends the token you send and returns a new one. A token lasts 7 days, and a session (one login and every token rotated from it, called a *family*) lasts at most 30 days. After that the user signs in again. The server stores only a SHA-256 hash of each refresh token.

**Two ways to hold the refresh token**, depending on the client:

| Client | How | Why |
|---|---|---|
| Native (the MAUI app, CLIs) | Default. The refresh token is in the response body. Keep it in the OS keychain (iOS/macOS Keychain, Android Keystore). Send it in the body to `/auth/refresh` and `/auth/revoke` | RFC 8252: there's no page script to steal it from, and the keychain protects it at rest |
| Browser | `POST /auth/login?useCookies=true`. The refresh token is set as a cookie `pf_refresh` (`HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`) and `refreshToken` is `null` in the body. Call `/auth/refresh` and `/auth/revoke` with **no body**, and the cookie is used and rotated | JavaScript can't read an HttpOnly cookie, so an XSS bug can't steal the session. `SameSite=Strict` means another site can't make the browser spend it. Keep the access token in memory only, never in `localStorage` |

### `POST /api/v1/auth/register`

Not authenticated.

**Request**
```json
{ "email": "user@example.com", "password": "at-least-8-chars" }
```
`email` must be a valid email address. `password` must be at least 8 characters.

**Response**
- `200 OK` — `"User registered successfully"`
- `400` — validation problem (per-field errors) if email/password fail validation
- `400` — Identity errors (e.g. email already registered)

### `POST /api/v1/auth/login`

Not authenticated. Rate-limited (10 per minute per IP).

**Query**: `useCookies=true` (optional, browser clients only, see above).

**Request**
```json
{ "email": "user@example.com", "password": "...", "twoFactorCode": "123456" }
```
`twoFactorCode` (6 digits) or `twoFactorRecoveryCode` (`XXXXX-XXXXX`) is only needed for accounts with two-factor authentication on. Send the login without one first; a `401` with code `RequiresTwoFactor` means "ask for it and send the same login again with it".

**Response**
- `200 OK`
  ```json
  { "token": "<jwt>", "refreshToken": "<opaque string>", "expiresIn": 600 }
  ```
- `401` problem with `"code": "RequiresTwoFactor"`: the password was right, a second factor is needed
- `401` problem with `"code": "InvalidTwoFactorCode"`: wrong, expired or **already used** code (each TOTP code works once)
  With `useCookies=true`, `refreshToken` is `null` and the token is in the `Set-Cookie: pf_refresh=...` header instead.
- `401 Unauthorized` — wrong email or password, or the account is locked out. Five wrong passwords or codes lock it for 15 minutes, and the response doesn't say which

### `POST /api/v1/auth/refresh`

Not authenticated (the refresh token itself is the credential). Rate-limited like login. Rotates the refresh token: the one you send is spent and a new one is issued, delivered the same way it arrived (body or cookie).

**Request**
```json
{ "refreshToken": "<opaque string>" }
```
Or no body at all, to use the `pf_refresh` cookie.

**Response**
- `200 OK` — same shape as login
- `401 Unauthorized` — no token, token not found, expired, past the 30-day session limit, logged out, **or already used**. Presenting a token that was already rotated is treated as theft: every token in that session's family is revoked, so whoever holds the newest one is signed out too. The user's other sessions (other logins and devices) are unaffected. Access tokens already issued stay valid until they expire (at most 10 minutes). A cookie-based refresh that fails also clears the cookie.

Clients must not refresh concurrently with the same token: the second request looks exactly like a replay and ends the session. The MAUI app funnels every refresh through one lock (`AuthTokenProvider.RefreshAsync`).

### `POST /api/v1/auth/revoke`

Not authenticated. Logout: ends this session (the token's whole family). Other devices stay signed in.

**Request**
```json
{ "refreshToken": "<opaque string>" }
```
Or no body, to use (and clear) the `pf_refresh` cookie.

**Response**
- `204 No Content`
- `400 Bad Request` — no token, token not found, or already revoked

## Two-factor authentication

TOTP (RFC 6238: 6 digits, 30-second steps), compatible with any authenticator app. All endpoints require auth and share login's rate limit. Wrong codes count toward the same lockout as wrong passwords.

| Endpoint | Body | Does |
|---|---|---|
| `GET /api/v1/auth/mfa` | — | `{ "enabled": true, "recoveryCodesLeft": 9 }` |
| `POST /api/v1/auth/mfa/setup` | — | New secret: `{ "sharedKey": "abcd efgh …", "authenticatorUri": "otpauth://totp/PersonalFinance:you@example.com?secret=…&issuer=PersonalFinance&digits=6&period=30" }`. Login doesn't change until `/enable`. `409` if already on |
| `POST /api/v1/auth/mfa/enable` | `{ "code": "123456" }` | Proves the app has the key and turns MFA on. Returns `{ "recoveryCodes": [10 × "XXXXX-XXXXX"] }`, **shown once**. `400` with `InvalidTwoFactorCode` if the code is wrong |
| `POST /api/v1/auth/mfa/disable` | `{ "code": "123456" }` or a recovery code | Turns MFA off and discards the secret and recovery codes. Needs a code, so a stolen access token alone can't strip the second factor |
| `POST /api/v1/auth/mfa/recovery-codes` | `{ "code": "123456" }` | Replaces the recovery codes, returning 10 new ones |

Each recovery code works once. An accepted TOTP code is remembered (hashed) and refused again for 3 minutes, so a code seen over someone's shoulder can't be replayed within its validity window.

## Audit

### `GET /api/v1/audit`

Requires auth. The signed-in user's own security events, newest first. Paged like `/transactions`: `page` (default 1) and `pageSize` (1-200, default 50).

**Response**
```json
{
  "items": [
    {
      "id": 42,
      "occurredAt": "2026-11-16T18:03:11Z",
      "eventType": "auth.refresh.reuse_detected",
      "ipAddress": "203.0.113.7",
      "userAgent": "PersonalFinance/0.3 (Mac Catalyst)",
      "details": { "sessionId": "8c1f…", "sessionRevoked": true }
    }
  ],
  "page": 1, "pageSize": 50, "totalCount": 1, "hasMore": false
}
```

| `eventType` | When | `details` |
|---|---|---|
| `auth.login.succeeded` | Password accepted | `sessionId` (the refresh-token family), `cookie` |
| `auth.login.failed` | Wrong password, or a locked-out account (recorded against the user). An unknown email is recorded with no user and is never shown here | `reason`: `wrong-password` / `locked-out` / `unknown-email` |
| `auth.refresh.reuse_detected` | A rotated refresh token was presented again: likely theft, and the session was ended | `sessionId`, `sessionRevoked` |
| `auth.logout` | `/auth/revoke` | `sessionId` |
| `auth.mfa.enabled` / `auth.mfa.disabled` | Two-factor turned on / off | — |
| `auth.mfa.failed` | Wrong code at login or when changing MFA settings | `reason`, `during` |
| `auth.mfa.recovery_code_used` | Signed in with a recovery code | `recoveryCodesLeft` |
| `auth.mfa.recovery_codes_regenerated` | New recovery codes | — |
| `plaid.item.linked` | A bank connected | `itemId`, `institutionId`, `accountCount` |
| `plaid.item.unlinked` | A bank disconnected | `itemId`, `accountIds`, `plaidNotified` (false if Plaid couldn't be told because the token no longer decrypted) |

Events never contain passwords, tokens, Plaid access tokens, or the email typed into a failed login. The table is append-only: the database rejects `UPDATE`, `DELETE` and `TRUNCATE` on it. Every event is also written as a structured log entry (`Audit {AuditEventType} …`).

## Plaid

All endpoints below require `Authorization: Bearer <token>`.

### `GET /api/v1/plaid/link-token`

Returns a Plaid Link token. The client passes this to Plaid Link's SDK to open the bank-connection UI.

**Response**
```json
{ "linkToken": "..." }
```

### `POST /api/v1/plaid/exchange-token`

Called after the user completes Plaid Link with a `public_token`. Exchanges it for an access token (encrypted before storage - never returned to the client), fetches the linked accounts, and persists them.

**Request**
```json
{ "publicToken": "public-sandbox-..." }
```

**Response**
- `200 OK` — `"Bank account linked successfully"`

### `POST /api/v1/plaid/webhook`

**Called by Plaid, not by clients.** Not authenticated with a JWT. Every request must instead carry Plaid's signed `Plaid-Verification` header, and anything that fails verification gets `401` and an audit event `plaid.webhook.rejected` (with the reason, no user):

1. The header is a JWT with `alg` exactly `ES256`. `none`, `HS256` and anything else are rejected before any key is looked at.
2. Its `kid` is a current Plaid key from `/webhook_verification_key/get`, cached for 24 h. A key Plaid has marked `expired_at` is refused.
3. The signature verifies with that key.
4. `iat` is no more than 5 minutes old, so a captured webhook can't be replayed later.
5. `request_body_sha256` equals the SHA-256 of the exact body received (constant-time compare), so the signature covers the payload.

Bodies over 64 KB get `413`. A verified `TRANSACTIONS` / `SYNC_UPDATES_AVAILABLE` queues a background sync of that Item (deduplicated while it waits) and returns `200` straight away. Other verified webhooks are logged and acknowledged with `200`, so Plaid doesn't retry them.

Plaid only sends webhooks if the link token named a URL: set `Plaid:WebhookUrl` to the public HTTPS address of this endpoint. It's unset in local development, where Plaid can't reach `localhost`, and the scheduled and manual syncs cover that case.

## Transactions

All endpoints below require `Authorization: Bearer <token>` and are scoped to the authenticated user's own data only.

### `GET /api/v1/transactions`

Paginated transaction list, optionally filtered by category.

**Query params** (all optional)
| Param | Type | Default | Notes |
|---|---|---|---|
| `page` | int | 1 | must be ≥ 1 |
| `pageSize` | int | 50 | must be 1–200 |
| `category` | string | (none) | exact match against `categoryPrimary`, e.g. `GENERAL_MERCHANDISE` |

**Response**
```json
{
  "items": [
    { "transactionId": 1, "amount": 42.50, "description": "Coffee Shop", "date": "2026-09-01T00:00:00Z", "categoryPrimary": "FOOD_AND_DRINK" }
  ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 686,
  "hasMore": true
}
```
- `400` — validation problem if `page`/`pageSize` out of range

### `GET /api/v1/transactions/summary`

Spend by category for a month. Excludes pending transactions.

**Query params**
| Param | Type | Default | Notes |
|---|---|---|---|
| `month` | string | current month | `yyyy-MM`, e.g. `2026-09` |

**Response**
```json
[
  { "category": "GENERAL_MERCHANDISE", "total": 1251.60, "transactionCount": 14 },
  { "category": "TRANSFER_IN", "total": -59.08, "transactionCount": 14 }
]
```
Sorted by `total` descending. Uncategorized transactions (`categoryPrimary` is null) appear as `"Uncategorized"`. `total` follows Plaid's sign convention: positive = money out, negative = money in - a category can be net-negative if refunds outweighed spend.
- `400` — validation problem if `month` isn't `yyyy-MM`

### `GET /api/v1/transactions/cashflow`

Income vs. expense for a month. Excludes pending transactions and internal transfers (`TRANSFER_IN`/`TRANSFER_OUT` - money moving between the user's own linked accounts, not real income or spending).

**Query params** — same `month` param as above.

**Response**
```json
{ "income": 7000.00, "expenses": 60192.44, "net": -53192.44 }
```
`income`/`expenses` are always ≥ 0. `net = income - expenses`.
- `400` — validation problem if `month` isn't `yyyy-MM`

### `GET /api/v1/transactions/categories`

Every `categoryPrimary` the user has at least one transaction in, across all months, sorted. Transactions without a category aren't represented. Use these values for `GET /transactions?category=`.

**Response**
```json
["FOOD_AND_DRINK", "INCOME", "TRAVEL"]
```

### `POST /api/v1/transactions/sync`

Triggers a Plaid cursor-based sync for every bank connection (Plaid Item) the user has linked, plus a balance refresh for each of its accounts. Idempotent to call repeatedly - each Item's cursor picks up only what changed since last sync.

A connection that fails (e.g. the bank needs the user to sign in again) doesn't stop the others; its accounts are listed in `failedAccountIds`.

**Response**
```json
{ "added": 686, "modified": 0, "removed": 0, "failedAccountIds": [] }
```
- `200 OK` — everything synced, or at least one connection did (check `failedAccountIds`)
- `502 Bad Gateway` — same body; every connection failed

### `DELETE /api/v1/transactions/{id}`

**Response**
- `204 No Content`
- `403 Forbidden` — transaction exists but belongs to a different user
- `404 Not Found` — transaction doesn't exist

## Accounts

Requires `Authorization: Bearer <token>`.

### `GET /api/v1/accounts/summary`

Balances overview: total balance, assets/liabilities split, net worth, and a per-account breakdown.

**Response**
```json
{
  "totalBalance": 250247.63,
  "totalAssets": 86541.74,
  "totalLiabilities": 163705.89,
  "netWorth": -77164.15,
  "accounts": [
    { "accountId": 97, "bankName": "ins_109508", "accountType": "Depository", "balance": 110.00, "classification": "Asset" },
    { "accountId": 108, "bankName": "ins_109508", "accountType": "Loan", "balance": 23211.33, "classification": "Liability" }
  ]
}
```
`classification` is derived from Plaid's account type: `Depository`/`Investment` → `Asset`, `Credit`/`Loan` → `Liability`, anything else → `Other` (excluded from `totalAssets`/`totalLiabilities`/`netWorth`). `netWorth = totalAssets - totalLiabilities`.

### `DELETE /api/v1/accounts/{id}`

Unlinks the bank connection behind the account: removes the Plaid Item (`/item/remove`) and deletes **every** account that came from that connection, plus their transactions. Plaid can only remove a whole Item, so there's no unlinking a single account of a multi-account bank.

**Response**
- `204 No Content`
- `404 Not Found` — no such account for this user
- `502 Bad Gateway` — Plaid refused to remove the Item; nothing was deleted, safe to retry

## Error shape

Anything not documented above with a specific shape returns [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) `ProblemDetails` on failure, e.g.:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "One or more validation errors occurred.", "status": 400, "errors": { "month": ["month must be in yyyy-MM format, e.g. 2026-09"] } }
```

Unhandled server errors return a generic `500` `ProblemDetails` with no internal details leaked (full exception is logged server-side only).
