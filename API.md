# API Reference

Backend base URL in local dev: `http://localhost:5140` (see `Properties/launchSettings.json` in `PersonalFinance.Api` if that changes). All routes are versioned under `/api/v1/`.

This document describes the API surface as it exists today, for building a client against it without reading the backend source. See [`CHANGELOG.md`](CHANGELOG.md) for what changed when.

## Auth

Everything under `/api/v1/transactions`, `/api/v1/accounts`, and `/api/v1/plaid` requires a JWT access token:

```
Authorization: Bearer <token>
```

Access tokens expire after 15 minutes. Use the refresh token to get a new pair without asking the user to log in again.

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

Not authenticated.

**Request**
```json
{ "email": "user@example.com", "password": "..." }
```

**Response**
- `200 OK`
  ```json
  { "token": "<jwt>", "refreshToken": "<opaque string>" }
  ```
- `401 Unauthorized` — wrong email or password

### `POST /api/v1/auth/refresh`

Not authenticated (the refresh token itself is the credential). Rotates the refresh token - the one you send is revoked, a new one is issued.

**Request**
```json
{ "refreshToken": "<opaque string>" }
```

**Response**
- `200 OK` — same shape as login: `{ "token": "...", "refreshToken": "..." }`
- `401 Unauthorized` — token not found, expired, **or already used** (reuse of a revoked token revokes the entire token family - every refresh token issued to that user is invalidated, forcing a fresh login)

### `POST /api/v1/auth/revoke`

Not authenticated. Effectively a logout - revokes a single refresh token.

**Request**
```json
{ "refreshToken": "<opaque string>" }
```

**Response**
- `204 No Content`
- `400 Bad Request` — token not found or already revoked

## Plaid

All endpoints below require `Authorization: Bearer <token>`.

### `GET /api/v1/plaid/link-token`

Returns a Plaid Link token. The client passes this to Plaid Link's SDK to open the bank-connection UI.

**Response**
```json
{ "link_token": "..." }
```

### `POST /api/v1/plaid/exchange-token`

Called after the user completes Plaid Link with a `public_token`. Exchanges it for an access token (encrypted before storage - never returned to the client), fetches the linked accounts, and persists them.

**Request**
```json
{ "publicToken": "public-sandbox-..." }
```

**Response**
- `200 OK` — `"Bank account linked successfully"`

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

### `POST /api/v1/transactions/sync`

Triggers a Plaid cursor-based sync for every account the user has linked, plus a balance refresh for each. Idempotent to call repeatedly - each account's cursor picks up only what changed since last sync.

**Response**
```json
{ "added": 686, "modified": 0, "removed": 0 }
```

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

## Error shape

Anything not documented above with a specific shape returns [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) `ProblemDetails` on failure, e.g.:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1", "title": "One or more validation errors occurred.", "status": 400, "errors": { "month": ["month must be in yyyy-MM format, e.g. 2026-09"] } }
```

Unhandled server errors return a generic `500` `ProblemDetails` with no internal details leaked (full exception is logged server-side only).
