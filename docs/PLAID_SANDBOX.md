# Plaid Sandbox for local/GUI development

No custom seed data needed - Plaid's Sandbox environment generates realistic
mock data for you when you link through the normal Plaid Link flow.

## Getting a Plaid Link session going

1. Get a link token: `GET /api/v1/plaid/link-token` (requires a logged-in user's Bearer token)
2. Open Plaid Link (web widget, or the Plaid Link SDK once wired into MAUI) with that token
3. When prompted for an institution, search for **"Platypus Bank"** (`institution_id: ins_109508`) - a Plaid test institution that supports the `transactions` product
4. When prompted for credentials, use username **`user_transactions_dynamic`** and **any password** - this seeds ~6 months of realistic, varied transaction history (multiple categories, pending transactions, a mix of account types) instead of Plaid's default sparse sample data
5. Complete the flow - Plaid Link returns a `public_token`
6. Exchange it: `POST /api/v1/plaid/exchange-token` with `{ "publicToken": "<public_token>" }`
7. Sync: `POST /api/v1/transactions/sync` - pulls in the transaction history and refreshes balances

Skipping Plaid Link entirely (e.g. scripting this for a test/seed run) - you
can get a `public_token` directly against Plaid's Sandbox API without a UI:

```bash
curl -X POST https://sandbox.plaid.com/sandbox/public_token/create \
  -H "Content-Type: application/json" \
  -d '{
    "client_id": "<Plaid:ClientId from user secrets>",
    "secret": "<Plaid:Secret from user secrets>",
    "institution_id": "ins_109508",
    "initial_products": ["transactions"]
  }'
```
This returns `{ "public_token": "public-sandbox-..." }`, which you then feed into step 6 above.

## Notes

- Sandbox transaction history generation is asynchronous - immediately calling `/transactions/sync` right after exchange-token may return `added: 0`. Wait ~10-15 seconds after linking before syncing for the first time.
- `ins_109508` in this session's testing consistently produced 14 accounts (mix of Depository, Credit, Investment, and Loan types) and 600-700 transactions spanning the last ~4 months - a good spread for exercising pagination, category filters, spend summaries, cash flow, and net worth all at once.
- To force a specific test scenario (e.g. a category-heavy month to test chart rendering, or a pending transaction to test summary/cashflow exclusion), Plaid's `/sandbox/transactions/create` endpoint lets you inject exact transactions into an existing Sandbox item. Not needed for general dev/testing - `user_transactions_dynamic` already gives enough variety - but useful for reproducing a specific edge case.
- Sandbox credentials (`Plaid:ClientId`, `Plaid:Secret`, `Plaid:Environment`) live in `dotnet user-secrets`, not committed to the repo - see the README's Local development section.

## Webhooks in Sandbox

Plaid can only deliver webhooks to a public HTTPS URL, so locally they're
off and the scheduled/manual syncs do the work. To try them:

1. Expose the API, e.g. `cloudflared tunnel --url http://localhost:5140`,
   and set `Plaid:WebhookUrl` to `https://<tunnel-host>/api/v1/plaid/webhook`.
2. Link a bank in the app. The link token now carries the webhook URL.
3. Fire one from Sandbox (needs the Item's access token, so use a test
   script or the Plaid dashboard rather than the app):
   `POST https://sandbox.plaid.com/sandbox/item/fire_webhook` with
   `{"client_id", "secret", "access_token", "webhook_code": "SYNC_UPDATES_AVAILABLE"}`.
4. The API logs `Plaid webhook TRANSACTIONS/SYNC_UPDATES_AVAILABLE for Item ...`
   and then the webhook-triggered sync. A request without a valid
   `Plaid-Verification` header shows up as `plaid.webhook.rejected` in
   `GET /api/v1/audit` data (no user) and the API log.
