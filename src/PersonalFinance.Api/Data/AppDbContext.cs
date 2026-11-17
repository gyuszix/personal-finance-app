using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Data;

public class AppDbContext : IdentityDbContext<User>
{
    private readonly string? _currentUserId;

    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor httpContextAccessor) : base (options)
    {
        _currentUserId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    public const int MoneyPrecision = 18;
    public const int MoneyScale = 2;

    public DbSet<Account> Accounts {get; set;}
    public DbSet<Transaction> Transactions {get; set;}
    public DbSet<RefreshToken> RefreshTokens {get; set;}
    public DbSet<BalanceSnapshot> BalanceSnapshots {get; set;}
    public DbSet<Budget> Budgets {get; set;}
    public DbSet<AuditEvent> AuditEvents {get; set;}

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Fail closed: with no signed-in user (background jobs, anonymous
        // requests) these match nothing. Code that genuinely needs every
        // user's rows must say so with IgnoreQueryFilters() (#56).
        modelBuilder.Entity<Transaction>().HasQueryFilter(t => t.UserId == _currentUserId);

        modelBuilder.Entity<Account>().HasQueryFilter(a => a.UserId == _currentUserId);

        modelBuilder.Entity<BalanceSnapshot>().HasQueryFilter(s => s.UserId == _currentUserId);

        modelBuilder.Entity<Budget>().HasQueryFilter(b => b.UserId == _currentUserId);

        modelBuilder.Entity<Account>().HasIndex(a => a.PlaidAccountId).IsUnique();

        // Plaid transaction IDs are globally unique. Without this, the old
        // per-account sync stored each transaction once per account (#53).
        modelBuilder.Entity<Transaction>().HasIndex(t => t.PlaidTransactionId).IsUnique();

        // Every read filters by user, almost all by a date range too, and the
        // list orders by date. Plans before/after in docs/DB_INDEXES.md (#113).
        modelBuilder.Entity<Transaction>().HasIndex(t => new { t.UserId, t.Date });
        modelBuilder.Entity<Account>().HasIndex(a => a.UserId);
        modelBuilder.Entity<Account>().HasIndex(a => a.PlaidItemId);

        // Real foreign keys: a transaction or snapshot can't outlive its
        // account. The FK also gives Transactions an AccountId index, which
        // unlinking needs; BalanceSnapshots' (AccountId, Date) already covers it.
        modelBuilder.Entity<Transaction>()
            .HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BalanceSnapshot>()
            .HasOne<Account>().WithMany().HasForeignKey(s => s.AccountId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BalanceSnapshot>().HasIndex(s => new { s.AccountId, s.Date }).IsUnique();
        modelBuilder.Entity<BalanceSnapshot>().HasIndex(s => new { s.UserId, s.Date });
        modelBuilder.Entity<BalanceSnapshot>().Property(s => s.UserId).IsRequired();

        modelBuilder.Entity<RefreshToken>().HasIndex(r => r.TokenHash).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasIndex(r => r.FamilyId);
        modelBuilder.Entity<RefreshToken>().Property(r => r.TokenHash).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<RefreshToken>().Property(r => r.RevokedReason).HasMaxLength(32);
        modelBuilder.Entity<RefreshToken>().Ignore(r => r.IsRevoked);

        modelBuilder.Entity<Budget>().HasIndex(b => new { b.UserId, b.Category }).IsUnique();

        // Read per user, newest first (#116). No query filter: the audit
        // endpoint filters explicitly, and security events without a user
        // (failed logins for unknown emails) must still be recordable.
        modelBuilder.Entity<AuditEvent>().HasIndex(e => new { e.UserId, e.OccurredAt });
        modelBuilder.Entity<AuditEvent>().Property(e => e.EventType).HasMaxLength(64).IsRequired();
        modelBuilder.Entity<AuditEvent>().Property(e => e.IpAddress).HasMaxLength(64);
        modelBuilder.Entity<AuditEvent>().Property(e => e.UserAgent).HasMaxLength(256);
        modelBuilder.Entity<AuditEvent>().Property(e => e.Details).HasColumnType("jsonb");
        modelBuilder.Entity<Budget>().Property(b => b.UserId).IsRequired();
        modelBuilder.Entity<Budget>().Property(b => b.Category).IsRequired().HasMaxLength(64);
        modelBuilder.Entity<Budget>().Property(b => b.MonthlyLimit).HasPrecision(12, 2);

        // Money is decimal with an explicit precision, never float/double and
        // never unconstrained numeric: 18 digits, 2 after the point (cents)
        // covers any balance we'll see. MoneyPrecisionTests enforces this for
        // every decimal column (#112).
        modelBuilder.Entity<Transaction>().Property(t => t.Amount).HasPrecision(MoneyPrecision, MoneyScale);
        modelBuilder.Entity<Account>().Property(a => a.Balance).HasPrecision(MoneyPrecision, MoneyScale);
        modelBuilder.Entity<BalanceSnapshot>().Property(s => s.Balance).HasPrecision(MoneyPrecision, MoneyScale);
    }
}
