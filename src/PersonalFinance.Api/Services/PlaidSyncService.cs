using Going.Plaid;
using Going.Plaid.Accounts;
using Going.Plaid.Transactions;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Services;

public class PlaidSyncService(PlaidClient plaid, AppDbContext db, ILogger<PlaidSyncService> logger, PlaidTokenProtector protector, PlaidInstitutionService institutions)
{
  // /transactions/sync is an Item-level Plaid call: one access token returns
  // the transactions for every account behind that bank connection. So
  // callers group accounts by Item and sync each group once, rather than
  // once per Account row (which fetched - and stored - every transaction N
  // times, all attributed to whichever account triggered the call).
  public static IEnumerable<List<Account>> GroupByItem(IEnumerable<Account> accounts) =>
    accounts.GroupBy(a => a.PlaidAccessToken).Select(g => g.ToList());

  // Syncs one Plaid Item. Every account passed in must share the same
  // PlaidAccessToken - use GroupByItem.
  public async Task<(int added, int modified, int removed)> SyncItemAsync(IReadOnlyList<Account> itemAccounts)
  {
    if (itemAccounts.Count == 0) return (0, 0, 0);
    if (itemAccounts.Select(a => a.PlaidAccessToken).Distinct().Count() > 1)
      throw new ArgumentException("All accounts must belong to the same Plaid Item", nameof(itemAccounts));

    int added = 0, modified = 0, removed = 0;
    var accessToken = protector.Unprotect(itemAccounts[0].PlaidAccessToken);
    var accountsByPlaidId = itemAccounts.ToDictionary(a => a.PlaidAccountId);

    // The cursor belongs to the Item, but it's stored per Account row. If the
    // rows disagree (e.g. an account was added to the Item later), restart
    // from the beginning - Added below is idempotent, so a full replay is safe.
    var cursors = itemAccounts.Select(a => a.SyncCursor).Distinct().ToList();
    var cursor = cursors.Count == 1 ? cursors[0] : null;
    bool hasMore;

    do
    {
      var response = await plaid.TransactionsSyncAsync(new TransactionsSyncRequest
      {
        AccessToken = accessToken,
        Cursor = cursor
      });

      var addedIds = response.Added.Select(t => t.TransactionId).ToList();
      var existingById = await db.Transactions
        .IgnoreQueryFilters()
        .Where(t => addedIds.Contains(t.PlaidTransactionId))
        .ToDictionaryAsync(t => t.PlaidTransactionId);

      foreach (var pt in response.Added)
      {
        if (!accountsByPlaidId.TryGetValue(pt.AccountId ?? "", out var account))
        {
          logger.LogWarning(
              "Skipping Plaid transaction {PlaidTransactionId}: unknown Plaid account {PlaidAccountId}",
              pt.TransactionId, pt.AccountId);
          continue;
        }

        // Already stored (a replayed cursor) - just make sure it's attributed
        // to the right account, which older per-account syncs got wrong.
        if (existingById.TryGetValue(pt.TransactionId ?? "", out var existing))
        {
          existing.AccountId = account.AccountId;
          existing.UserId = account.UserId;
          continue;
        }

        db.Transactions.Add(new Transaction
        {
          AccountId = account.AccountId,
          UserId = account.UserId,
          Amount = (decimal)pt.Amount,
          Description = pt.MerchantName ?? pt.Name ?? "Unknown",
          Date = pt.Date.HasValue
            ? DateTime.SpecifyKind(pt.Date.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
            : DateTime.UtcNow,
          PlaidTransactionId = pt.TransactionId,
          IsPending = pt.Pending ?? false,
          PendingTransactionId = pt.PendingTransactionId,
          CategoryPrimary = pt.PersonalFinanceCategory?.Primary,
          CategoryDetailed = pt.PersonalFinanceCategory?.Detailed
        });
        added++;
      }

      foreach (var pt in  response.Modified)
      {
        var existing = await db.Transactions
          .IgnoreQueryFilters()
          .FirstOrDefaultAsync(t => t.PlaidTransactionId == pt.TransactionId);

        if (existing is null) continue;

        existing.Amount = (decimal)pt.Amount;
        existing.Description = pt.MerchantName ?? pt.Name ?? "Unknown";
        existing.IsPending = pt.Pending ?? false;
        existing.CategoryPrimary = pt.PersonalFinanceCategory?.Primary;
        existing.CategoryDetailed = pt.PersonalFinanceCategory?.Detailed;
        modified++;
      }

      foreach (var rt in response.Removed)
      {
        var existing = await db.Transactions
          .IgnoreQueryFilters()
          .FirstOrDefaultAsync(t => t.PlaidTransactionId == rt.TransactionId);

        if (existing is not null)
        {
          db.Transactions.Remove(existing);
          removed++;
        }
      }

      cursor = response.NextCursor;
      hasMore = response.HasMore;
    } while (hasMore);

    // Refresh the cached balances - Account.Balance is otherwise only ever
    // set once, at link time, and would go stale forever without this.
    var balanceResponse = await plaid.AccountsBalanceGetAsync(new AccountsBalanceGetRequest
    {
      AccessToken = accessToken
    });

    foreach (var plaidAccount in balanceResponse.Accounts)
    {
      if (accountsByPlaidId.TryGetValue(plaidAccount.AccountId, out var account))
        account.Balance = (decimal)(plaidAccount.Balances.Current ?? 0);
    }

    foreach (var account in itemAccounts)
      account.SyncCursor = cursor;

    // Accounts linked before bank names were resolved still hold Plaid's
    // raw institution ID - fix them up here, once per Item.
    var rawInstitutionId = itemAccounts.Select(a => a.BankName).FirstOrDefault(PlaidInstitutionService.IsRawInstitutionId);
    if (rawInstitutionId is not null && await institutions.GetNameAsync(rawInstitutionId) is { } bankName)
    {
      foreach (var account in itemAccounts.Where(a => a.BankName == rawInstitutionId))
        account.BankName = bankName;
    }

    await db.SaveChangesAsync();

    logger.LogInformation(
        "Synced Plaid Item ({AccountCount} accounts: {AccountIds}): {Added} added, {Modified} modified, {Removed} removed, balances refreshed",
        itemAccounts.Count, itemAccounts.Select(a => a.AccountId), added, modified, removed);
    return (added, modified, removed);
  }
}
