using Going.Plaid;
using Going.Plaid.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Api.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this WebApplication app)
    {
        app.MapGet("/transactions", async (
            PlaidClient plaid,
            AppDbContext db,
            UserManager<User> userManager,
            HttpContext http,
            ILogger<Program> logger) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            logger.LogInformation("Fetching transactions for user {UserId}", userId);

            var accounts = await db.Accounts
                .Where(a => a.UserId == userId)
                .ToListAsync();

            if (!accounts.Any())
            {
                logger.LogInformation("User {UserId} has no linked accounts", userId);
                return Results.Ok(new List<TransactionResponse>());
            }

            var newTransactionCount = 0;

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
                        newTransactionCount++;
                    }
                }
            }

            await db.SaveChangesAsync();

            logger.LogInformation(
                "Synced {NewCount} new transactions for user {UserId} across {AccountCount} accounts",
                newTransactionCount, userId, accounts.Count
            );

            var transactions = await db.Transactions
                .Where(t => accounts.Select(a => a.AccountId).Contains(t.AccountId))
                .Select(t => new TransactionResponse
                {
                    TransactionId = t.TransactionId,
                    Amount = t.Amount,
                    Description = t.Description,
                    Date = t.Date
                })
                .ToListAsync();

            logger.LogInformation("Returned {Count} transactions for user {UserId}", transactions.Count, userId);

            return Results.Ok(transactions);
        }).RequireAuthorization();

        app.MapDelete("/transactions/{id}", async (
            int id,
            AppDbContext db,
            IAuthorizationService authorizationService,
            HttpContext http,
            ILogger<Program> logger) =>
        {
            var transaction = await db.Transactions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TransactionId == id);

            if (transaction == null)
            {
                logger.LogWarning("Transaction {TransactionId} not found for delete request", id);
                return Results.NotFound();
            }

            var auth = await authorizationService.AuthorizeAsync(
                http.User, transaction, "ResourceOwner");

            if (!auth.Succeeded)
            {
                logger.LogWarning(
                    "User {UserId} forbidden from deleting transaction {TransactionId}",
                    http.User.Identity?.Name, id);
                return Results.Forbid();
            }

            db.Transactions.Remove(transaction);
            await db.SaveChangesAsync();

            logger.LogInformation("Transaction {TransactionId} deleted", id);

            return Results.NoContent();
        }).RequireAuthorization();
    }
}