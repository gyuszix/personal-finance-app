namespace PersonalFinance.Api.Data.Entities;

// Mirrored Plaid transaction. Unique index on plaid_transaction_id.
// Plaid-owned columns (amount, merchant, personal_finance_category, pending, ...)
// are written only by sync; nullable user_category_override wins when set.
public class Transaction
{
}
