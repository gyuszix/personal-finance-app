using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Services.Insights;

// Spending split by the kind of account the money left from (#87): a credit
// card ("Credit") or a bank account ("Depository", i.e. debit). Spending
// from loan or investment accounts is in neither.
public static class PaymentMethod
{
    public const string Credit = "Credit";
    public const string Debit = "Depository";

    public record SpendRow(string AccountType, string Category, decimal Amount, DateTime Date);

    // The spending in [start, end) that counts toward cash flow, with the
    // account type it was paid from.
    public static Task<List<SpendRow>> SpendingAsync(
        AppDbContext db, DateTime start, DateTime end, CancellationToken cancellationToken) =>
        (from t in db.Transactions.CountedInCashflow()
         join a in db.Accounts on t.AccountId equals a.AccountId
         where t.Amount > 0 && t.Date >= start && t.Date < end
               && (a.AccountType == Credit || a.AccountType == Debit)
         select new SpendRow(a.AccountType, t.CategoryPrimary ?? "Uncategorized", t.Amount, t.Date))
        .ToListAsync(cancellationToken);

    // Card payments made in [start, end). A payment shows up twice when both
    // ends are linked - leaving the bank, and arriving on the card - so the
    // bank side is used, falling back to the card side only when no bank
    // side was recorded.
    public static async Task<decimal> CardPaymentsAsync(
        AppDbContext db, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var payments = await (
            from t in db.Transactions
            join a in db.Accounts on t.AccountId equals a.AccountId
            where !t.IsPending && t.CategoryDetailed == Spending.CreditCardPayment
                  && t.Date >= start && t.Date < end
            select new { a.AccountType, t.Amount })
            .ToListAsync(cancellationToken);

        var fromBank = payments.Where(p => p.AccountType == Debit && p.Amount > 0).Sum(p => p.Amount);
        if (fromBank > 0) return fromBank;
        return payments.Where(p => p.AccountType == Credit && p.Amount < 0).Sum(p => -p.Amount);
    }
}
