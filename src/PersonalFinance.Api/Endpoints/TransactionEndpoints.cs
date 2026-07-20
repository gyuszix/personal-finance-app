using Going.Plaid;
using Going.Plaid.Transactions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this WebApplication app)
    {
        app.MapGet("/transactions", async (
            PlaidClient plaid,
            AppDbContext db,
            UserManager<User> userManager,
            HttpContext http) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            var accounts = await db.Accounts
                .Where(a => a.UserId == userId)
                .ToListAsync();

            if (!accounts.Any())
                return Results.Ok(new List<Transaction>());

            foreach (var account in accounts)
            {
                var response = await plaid.TransactionsGetAsync(new TransactionsGetRequest
                {
                    AccessToken = account.PlaidAccessToken,
                    StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)),
                    EndDate = DateOnly.FromDateTime(DateTime.UtcNow)
                });

                foreach (var pt in response.Transactions)
                {
                    var exists = await db.Transactions
                        .AnyAsync(t => t.PlaidTransactionId == pt.TransactionId);

                    if (!exists)
                    {
                        db.Transactions.Add(new Transaction
                        {
                            AccountId = account.AccountId,
                            Amount = (decimal)pt.Amount,
                            Description = pt.MerchantName ?? pt.Name ?? "Unknown",
                            Date = pt.Date.HasValue
                                ? pt.Date.Value.ToDateTime(TimeOnly.MinValue)
                                : DateTime.UtcNow,
                            PlaidTransactionId = pt.TransactionId
                        });
                    }
                }
            }

            await db.SaveChangesAsync();

            var transactions = await db.Transactions
                .Where(t => accounts.Select(a => a.AccountId).Contains(t.AccountId))
                .ToListAsync();

            return Results.Ok(transactions);
        }).RequireAuthorization();
    }
}