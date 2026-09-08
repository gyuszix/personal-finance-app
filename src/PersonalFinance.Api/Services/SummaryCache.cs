using Microsoft.Extensions.Caching.Memory;

namespace PersonalFinance.Api.Services;

// Thin wrapper around IMemoryCache for the read-heavy summary endpoints
// (accounts/summary, transactions/summary, transactions/cashflow). One
// place that owns the cache key shapes, so both the endpoints (that read/
// write the cache) and anything that triggers a sync (that needs to
// invalidate it) agree on the same keys.
public class SummaryCache(IMemoryCache cache)
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    public string AccountsSummaryKey(string userId) => $"accounts-summary:{userId}";

    public string TransactionsSummaryKey(string userId, DateTime periodStart) =>
        $"transactions-summary:{userId}:{periodStart:yyyy-MM}";

    public string CashflowKey(string userId, DateTime periodStart) =>
        $"cashflow:{userId}:{periodStart:yyyy-MM}";

    public bool TryGet<T>(string key, out T? value) => cache.TryGetValue(key, out value);

    public void Set<T>(string key, T value) =>
        cache.Set(key, value, DefaultTtl);

    // Called after any sync (manual or scheduled) completes for a user.
    // Only the current month's summary/cashflow are explicitly evicted -
    // new transactions from a normal sync land in the current month almost
    // always. Anything else cached (a past month someone was viewing) just
    // expires on its own TTL shortly after, which is an acceptable
    // trade-off for how simple this keeps the invalidation logic.
    public void InvalidateForUser(string userId)
    {
        cache.Remove(AccountsSummaryKey(userId));

        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        cache.Remove(TransactionsSummaryKey(userId, currentMonth));
        cache.Remove(CashflowKey(userId, currentMonth));
    }
}
