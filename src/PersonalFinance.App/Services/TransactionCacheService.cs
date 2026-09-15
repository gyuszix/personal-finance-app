using PersonalFinance.Shared.DTOs;
using SQLite;

namespace PersonalFinance.App.Services;

// Local offline cache of the last-synced transactions list, so the
// Transactions tab can show stale data (with a "last synced" indicator)
// instead of an empty screen when the network call fails.
public class TransactionCacheService
{
    private readonly SQLiteAsyncConnection _db;

    public TransactionCacheService()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "transactions_cache.db3");
        _db = new SQLiteAsyncConnection(dbPath);
    }

    private async Task EnsureTablesCreatedAsync()
    {
        await _db.CreateTableAsync<CachedTransaction>();
        await _db.CreateTableAsync<CacheMetadata>();
    }

    public async Task ReplaceTransactionsAsync(IEnumerable<TransactionResponse> transactions)
    {
        await EnsureTablesCreatedAsync();

        var cached = transactions.Select(t => new CachedTransaction
        {
            TransactionId = t.TransactionId,
            Amount = t.Amount,
            Description = t.Description,
            Date = t.Date,
            CategoryPrimary = t.CategoryPrimary
        }).ToList();

        await _db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<CachedTransaction>();
            conn.InsertAll(cached);
            conn.InsertOrReplace(new CacheMetadata { Id = 1, LastSyncedAt = DateTime.UtcNow });
        });
    }

    public async Task<List<TransactionResponse>> GetCachedTransactionsAsync()
    {
        await EnsureTablesCreatedAsync();

        var cached = await _db.Table<CachedTransaction>()
            .OrderByDescending(t => t.Date)
            .ToListAsync();

        return cached.Select(c => new TransactionResponse
        {
            TransactionId = c.TransactionId,
            Amount = c.Amount,
            Description = c.Description,
            Date = c.Date,
            CategoryPrimary = c.CategoryPrimary
        }).ToList();
    }

    public async Task<DateTime?> GetLastSyncedAtAsync()
    {
        await EnsureTablesCreatedAsync();

        var meta = await _db.Table<CacheMetadata>().FirstOrDefaultAsync();
        return meta?.LastSyncedAt;
    }
}

[Table("CachedTransactions")]
public class CachedTransaction
{
    [PrimaryKey]
    public int TransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? CategoryPrimary { get; set; }
}

[Table("CacheMetadata")]
public class CacheMetadata
{
    [PrimaryKey]
    public int Id { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
