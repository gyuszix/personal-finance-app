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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Fail closed: with no signed-in user (background jobs, anonymous
        // requests) these match nothing. Code that genuinely needs every
        // user's rows must say so with IgnoreQueryFilters() (#56).
        modelBuilder.Entity<Transaction>().HasQueryFilter(t => t.UserId == _currentUserId);

        modelBuilder.Entity<Account>().HasQueryFilter(a => a.UserId == _currentUserId);

        modelBuilder.Entity<Account>().HasIndex(a => a.PlaidAccountId).IsUnique();
    }
}
