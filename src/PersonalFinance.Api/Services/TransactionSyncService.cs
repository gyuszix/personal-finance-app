namespace PersonalFinance.Api.Services;

// BackgroundService consuming an in-memory Channel<SyncRequest> fed by the
// Plaid webhook endpoint and by initial link (6-month backfill).
// Applies /transactions/sync results: upsert on plaid_transaction_id,
// replace pending via pending_transaction_id, delete removed, advance cursor,
// update cached balances.
public class TransactionSyncService
{
}
