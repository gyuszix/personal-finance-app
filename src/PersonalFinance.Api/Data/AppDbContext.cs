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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Transaction>().HasQueryFilter(t => _currentUserId == null || t.UserId == _currentUserId);

        modelBuilder.Entity<Account>().HasQueryFilter(a => _currentUserId == null || a.UserId == _currentUserId);

    }
}