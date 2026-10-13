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

    public DbSet<Account> Accounts {get; set;}
    public DbSet<Transaction> Transactions {get; set;}
    public DbSet<RefreshToken> RefreshTokens {get; set;}
    public DbSet<BalanceSnapshot> BalanceSnapshots {get; set;}
    public DbSet<Budget> Budgets {get; set;}

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

        modelBuilder.Entity<BalanceSnapshot>().HasIndex(s => new { s.AccountId, s.Date }).IsUnique();
        modelBuilder.Entity<BalanceSnapshot>().HasIndex(s => new { s.UserId, s.Date });
        modelBuilder.Entity<BalanceSnapshot>().Property(s => s.UserId).IsRequired();

        modelBuilder.Entity<Budget>().HasIndex(b => new { b.UserId, b.Category }).IsUnique();
        modelBuilder.Entity<Budget>().Property(b => b.UserId).IsRequired();
        modelBuilder.Entity<Budget>().Property(b => b.Category).IsRequired().HasMaxLength(64);
        modelBuilder.Entity<Budget>().Property(b => b.MonthlyLimit).HasPrecision(12, 2);
    }
}
