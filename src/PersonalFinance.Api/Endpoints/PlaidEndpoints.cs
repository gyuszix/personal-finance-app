namespace PersonalFinance.Api.Endpoints;

// POST /api/v1/plaid/link-token   — create a Link token for the client
// POST /api/v1/plaid/exchange     — exchange public_token, store encrypted access_token, kick off backfill
// POST /api/v1/plaid/webhook      — verify + enqueue sync on SYNC_UPDATES_AVAILABLE (anonymous endpoint)
public static class PlaidEndpoints
{
}
