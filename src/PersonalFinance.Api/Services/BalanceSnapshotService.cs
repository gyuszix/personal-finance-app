using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Services;

public class BalanceSnapshotService(AppDbContext db, TimeProvider clock)
{
  // Stages today's balance for each account - an update if today already
  // has a snapshot, so the last sync of the day wins. Doesn't save: the
  // caller saves it together with the balances it came from.
  public async Task RecordAsync(IReadOnlyCollection<Account> accounts)
  {
    var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    var accountIds = accounts.Select(a => a.AccountId).ToList();

    // Runs from the background sync too, where there's no signed-in user.
    var existing = await db.BalanceSnapshots
      .IgnoreQueryFilters()
      .Where(s => s.Date == today && accountIds.Contains(s.AccountId))
      .ToDictionaryAsync(s => s.AccountId);

    foreach (var account in accounts)
    {
      if (existing.TryGetValue(account.AccountId, out var snapshot))
      {
        snapshot.Balance = account.Balance;
        continue;
      }

      db.BalanceSnapshots.Add(new BalanceSnapshot
      {
        AccountId = account.AccountId,
        UserId = account.UserId,
        Date = today,
        Balance = account.Balance
      });
    }
  }
}
