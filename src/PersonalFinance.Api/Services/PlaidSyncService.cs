using Going.Plaid;
using Going.Plaid.Accounts;
using Going.Plaid.Transactions;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Services;

public class PlaidSyncService(PlaidClient plaid, AppDbContext db, ILogger<PlaidSyncService> logger, PlaidTokenProtector protector)
{
  public async Task<(int added, int modified, int removed)> SyncAccountAsync(Account account)
  {
    int added = 0, modified = 0, removed = 0;
    var cursor = account.SyncCursor;
    var accessToken = protector.Unprotect(account.PlaidAccessToken);
    bool hasMore;

    do
    {
      var response = await plaid.TransactionsSyncAsync(new TransactionsSyncRequest
      {
        AccessToken = accessToken,
        Cursor = cursor
      });

      foreach (var pt in response.Added)
      {
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

    account.SyncCursor = cursor;

    // Refresh the cached balance - Account.Balance is otherwise only ever
    // set once, at link time, and would go stale forever without this.
    var balanceResponse = await plaid.AccountsBalanceGetAsync(new AccountsBalanceGetRequest
    {
      AccessToken = accessToken
    });

    var matchingAccount = balanceResponse.Accounts
      .FirstOrDefault(a => a.AccountId == account.PlaidAccountId);

    if (matchingAccount is not null)
    {
      account.Balance = (decimal)(matchingAccount.Balances.Current ?? 0);
    }

    await db.SaveChangesAsync();

    logger.LogInformation(
        "Synced account {AccountId}: {Added} added, {Modified} modified, {Removed} removed, balance refreshed to {Balance}",
        account.AccountId, added, modified, removed, account.Balance);
    return (added, modified, removed);
  }
}
          
