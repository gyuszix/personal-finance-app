using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// Net worth per day for up to a year, ending today or at the end of the
// selected month, from the daily balance snapshots (#84). A day an account
// has no snapshot for (e.g. its sync failed) carries its last known balance
// forward. Days before the first snapshot aren't returned - there's no
// history from before snapshots began.
public class NetWorthTrendInsight(AppDbContext db, TimeProvider clock) : IInsight<NetWorthTrendResponse>
{
    public static string Key => "net-worth-trend";

    public const int Days = 365;

    public async Task<NetWorthTrendResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var monthEnd = DateOnly.FromDateTime(month.End).AddDays(-1);
        var end = monthEnd < today ? monthEnd : today;
        var start = end.AddDays(-(Days - 1));

        var accountTypes = await db.Accounts
            .ToDictionaryAsync(a => a.AccountId, a => AccountClassification.Of(a.AccountType), cancellationToken);

        var inRange = await db.BalanceSnapshots
            .Where(s => s.Date >= start && s.Date <= end)
            .Select(s => new { s.AccountId, s.Date, s.Balance })
            .ToListAsync(cancellationToken);

        // Each account's last balance before the range, to carry into it.
        var before = await db.BalanceSnapshots
            .Where(s => s.Date < start)
            .GroupBy(s => s.AccountId)
            .Select(g => g.OrderByDescending(s => s.Date).Select(s => new { s.AccountId, s.Date, s.Balance }).First())
            .ToListAsync(cancellationToken);

        var byDay = inRange.ToLookup(s => s.Date);
        var latest = before.ToDictionary(s => s.AccountId, s => s.Balance);
        var points = new List<NetWorthPoint>();

        for (var day = start; day <= end; day = day.AddDays(1))
        {
            foreach (var snapshot in byDay[day])
                latest[snapshot.AccountId] = snapshot.Balance;
            if (latest.Count == 0) continue;

            decimal assets = 0, liabilities = 0;
            foreach (var (accountId, balance) in latest)
            {
                var classification = accountTypes.GetValueOrDefault(accountId, AccountClassification.Other);
                if (classification == AccountClassification.Asset) assets += balance;
                else if (classification == AccountClassification.Liability) liabilities += balance;
            }

            points.Add(new NetWorthPoint
            {
                Date = day.ToString("yyyy-MM-dd"),
                Assets = assets,
                Liabilities = liabilities,
                NetWorth = assets - liabilities
            });
        }

        return new NetWorthTrendResponse { End = end.ToString("yyyy-MM-dd"), Points = points };
    }
}
