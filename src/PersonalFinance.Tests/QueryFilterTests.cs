using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Tests;

// AppDbContext's per-user filters must fail closed: with no signed-in user
// (a background job, an anonymous request) they match nothing, so a missing
// RequireAuthorization() can't expose every user's data (#56).
public class QueryFilterTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task WithoutSignedInUser_FiltersMatchNothing()
    {
        var plaidAccountId = Guid.NewGuid().ToString();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = new Account
            {
                UserId = Guid.NewGuid().ToString(),
                BankName = "Test Bank",
                AccountType = "depository",
                PlaidAccountId = plaidAccountId,
                PlaidAccessToken = "protected-token"
            };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            db.Transactions.Add(new Transaction
            {
                AccountId = account.AccountId,
                UserId = account.UserId,
                Amount = 12.34m,
                Description = "Coffee",
                Date = DateTime.UtcNow,
                PlaidTransactionId = Guid.NewGuid().ToString()
            });
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            // No HttpContext in a bare scope - same as ScheduledPlaidSyncService.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.Empty(await db.Accounts.ToListAsync());
            Assert.Empty(await db.Transactions.ToListAsync());

            // Opting out explicitly still sees the row.
            Assert.Contains(
                await db.Accounts.IgnoreQueryFilters().ToListAsync(),
                a => a.PlaidAccountId == plaidAccountId);
        }
    }
}
