using Going.Plaid;
using Going.Plaid.Accounts;
using Going.Plaid.Transactions;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Services;

public class PlaidSyncService(PlaidClient plaid, AppDbContext db, ILogger<PlaidSyncService> logger, PlaidTokenProtector protector, PlaidInstitutionService institutions, BalanceSnapshotService snapshots)
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

    try
    {
      // One DB transaction per Item sync: the transactions, balances and the
      // cursor that produced them commit together or not at all (#114).
      await using var tx = await db.Database.BeginTransactionAsync();
      var lockedAccounts = await LockItemAsync(itemAccounts);
      if (lockedAccounts.Count == 0) return (0, 0, 0);
      var result = await SyncItemCoreAsync(lockedAccounts);
      await tx.CommitAsync();
      return result;
    }
    catch
    {
      // Callers sync several Items on one DbContext and carry on past a
      // failure, so drop anything this Item staged but never saved - or the
      // next Item's SaveChangesAsync would persist it (half a page of
      // transactions, a cursor that was never reached).
      DiscardUnsavedChanges();
      throw;
    }
  }

  // Two syncs of one Item at once (the schedule, a manual sync, a webhook,
  // another API instance) would both start from the same cursor, fetch the
  // same pages and race to insert the same transactions. Lock the Item's
  // account rows for the rest of the transaction instead: a second syncer
  // waits here until the first commits, then reloads the cursor the first
  // one saved and carries on from there, so each page is applied once.
  // Ordered by id so two lockers can't deadlock over a shared row.
  // Returns the accounts that still exist once the lock is held - one
  // unlinked while we waited is gone (ReloadAsync detaches it).
  private async Task<List<Account>> LockItemAsync(IReadOnlyList<Account> itemAccounts)
  {
    var ids = itemAccounts.Select(a => a.AccountId).Order().ToArray();
    await db.Database.ExecuteSqlAsync(
      $"""SELECT 1 FROM "Accounts" WHERE "AccountId" = ANY({ids}) ORDER BY "AccountId" FOR UPDATE""");

    foreach (var account in itemAccounts)
      await db.Entry(account).ReloadAsync();

    return itemAccounts.Where(a => db.Entry(a).State != EntityState.Detached).ToList();
  }

  private void DiscardUnsavedChanges()
  {
    foreach (var entry in db.ChangeTracker.Entries().ToList())
    {
      if (entry.State == EntityState.Added)
        entry.State = EntityState.Detached;
      else if (entry.State is EntityState.Modified or EntityState.Deleted)
        entry.State = EntityState.Unchanged;
    }
  }

  private async Task<(int added, int modified, int removed)> SyncItemCoreAsync(IReadOnlyList<Account> itemAccounts)
  {

    int added = 0, modified = 0, removed = 0;
    var accessToken = protector.Unprotect(itemAccounts[0].PlaidAccessToken);
    var accountsByPlaidId = itemAccounts.ToDictionary(a => a.PlaidAccountId);

    // The cursor belongs to the Item, but it's stored per Account row. If the
    // rows disagree (e.g. an account was added to the Item later), restart
    // from the beginning - Added below is idempotent, so a full replay is safe.
    var cursors = itemAccounts.Select(a => a.SyncCursor).Distinct().ToList();
    var cursor = cursors.Count == 1 ? cursors[0] : null;
    bool hasMore;

    // Every transaction this sync has touched, saved or only tracked so far,
    // by Plaid ID - across all pages. A pending transaction added on one page
    // and removed on a later one (it posted) exists only here until the save.
    var known = new Dictionary<string, Transaction>();
    async Task LoadKnownAsync(IEnumerable<string?> plaidIds)
    {
      var missing = plaidIds.OfType<string>().Where(id => !known.ContainsKey(id)).Distinct().ToList();
      if (missing.Count == 0) return;
      foreach (var stored in await db.Transactions.IgnoreQueryFilters()
                 .Where(t => missing.Contains(t.PlaidTransactionId)).ToListAsync())
        known[stored.PlaidTransactionId] = stored;
    }

    do
    {
      var response = await plaid.TransactionsSyncAsync(new TransactionsSyncRequest
      {
        AccessToken = accessToken,
        Cursor = cursor
      });

      // Going.Plaid reports API errors on the response instead of throwing,
      // leaving Added/Modified/Removed null.
      if (response.Error is not null)
        throw new InvalidOperationException(
          $"Plaid /transactions/sync failed: {response.Error.ErrorCode} {response.Error.ErrorMessage}");

      // Added and modified are both upserts. Plaid can replay a page we've
      // already applied (a cursor that was reset, or rows that disagree), and
      // can report a modification of a transaction we never stored (e.g. one
      // added before this account was linked to the app): either way the
      // latest version wins and each Plaid transaction stays one row.
      var changed = response.Added.Select(t => (Plaid: t, IsModification: false))
        .Concat(response.Modified.Select(t => (Plaid: t, IsModification: true)))
        .ToList();
      await LoadKnownAsync(changed.Select(c => c.Plaid.TransactionId).Concat(response.Removed.Select(r => r.TransactionId)));

      foreach (var (pt, isModification) in changed)
      {
        if (!accountsByPlaidId.TryGetValue(pt.AccountId ?? "", out var account))
        {
          logger.LogWarning(
              "Skipping Plaid transaction {PlaidTransactionId}: unknown Plaid account {PlaidAccountId}",
              pt.TransactionId, pt.AccountId);
          continue;
        }

        if (!known.TryGetValue(pt.TransactionId ?? "", out var transaction))
        {
          transaction = new Transaction { PlaidTransactionId = pt.TransactionId! };
          db.Transactions.Add(transaction);
          known[transaction.PlaidTransactionId] = transaction;
          added++;
        }
        else if (isModification)
        {
          modified++;
        }

        // Also re-attributes rows older per-account syncs stored against the
        // wrong account.
        transaction.AccountId = account.AccountId;
        transaction.UserId = account.UserId;
        transaction.Amount = pt.Amount ?? 0m;
        transaction.Description = pt.MerchantName ?? pt.Name ?? "Unknown";
        transaction.Date = pt.Date.HasValue
          ? DateTime.SpecifyKind(pt.Date.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
          : transaction.Date == default ? DateTime.UtcNow : transaction.Date;
        transaction.IsPending = pt.Pending ?? false;
        transaction.PendingTransactionId = pt.PendingTransactionId;
        transaction.CategoryPrimary = pt.PersonalFinanceCategory?.Primary;
        transaction.CategoryDetailed = pt.PersonalFinanceCategory?.Detailed;
      }

      foreach (var rt in response.Removed)
      {
        if (rt.TransactionId is null || !known.Remove(rt.TransactionId, out var gone)) continue;
        db.Transactions.Remove(gone);
        removed++;
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

    if (balanceResponse.Error is not null)
      throw new InvalidOperationException(
        $"Plaid /accounts/balance/get failed: {balanceResponse.Error.ErrorCode} {balanceResponse.Error.ErrorMessage}");

    // Webhooks name the Item, not the account (#114); backfills accounts
    // linked before the Item ID was stored.
    var itemId = balanceResponse.Item?.ItemId;

    foreach (var plaidAccount in balanceResponse.Accounts)
    {
      if (accountsByPlaidId.TryGetValue(plaidAccount.AccountId, out var account))
      {
        account.Balance = (decimal)(plaidAccount.Balances.Current ?? 0);
        // Also backfills accounts linked before the subtype was stored.
        account.Subtype = plaidAccount.Subtype?.ToString();
        account.PlaidItemId ??= itemId;
      }
    }

    foreach (var account in itemAccounts)
      account.SyncCursor = cursor;

    await snapshots.RecordAsync(itemAccounts);

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
