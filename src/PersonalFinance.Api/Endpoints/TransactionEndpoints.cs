using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.DTOs;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Middleware;
using PersonalFinance.Api.Services;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Api.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/transactions", async (
            [AsParameters] TransactionQueryParameters query,
            AppDbContext db,
            UserManager<User> userManager,
            HttpContext http,
            ILogger<Program> logger) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            var transactionsQuery = db.Transactions.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Category))
            {
                transactionsQuery = transactionsQuery.Where(t => t.CategoryPrimary == query.Category);
            }

            var page = query.ResolvedPage;
            var pageSize = query.ResolvedPageSize;
            var totalCount = await transactionsQuery.CountAsync();

            var transactions = await transactionsQuery
                .OrderByDescending(t => t.Date)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new TransactionResponse
                {
                    TransactionId = t.TransactionId,
                    Amount = t.Amount,
                    Description = t.Description,
                    Date = t.Date,
                    CategoryPrimary = t.CategoryPrimary
                })
                .ToListAsync();

            logger.LogInformation(
                "Returned {Count} of {TotalCount} transactions for user {UserId} (page {Page}, category {Category})",
                transactions.Count, totalCount, userId, page, query.Category ?? "(none)");

            return Results.Ok(new PagedResult<TransactionResponse>
            {
                Items = transactions,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                HasMore = page * pageSize < totalCount
            });
        })
        .RequireAuthorization()
        .AddEndpointFilter<ValidationFilter<TransactionQueryParameters>>();

        app.MapPost("/transactions/sync", async (
            PlaidSyncService syncService,
            AppDbContext db,
            UserManager<User> userManager,
            HttpContext http) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            var accounts = await db.Accounts.Where(a => a.UserId == userId).ToListAsync();

            int totalAdded = 0, totalModified = 0, totalRemoved = 0;
            foreach (var account in accounts)
            {
                var (added, modified, removed) = await syncService.SyncAccountAsync(account);
                totalAdded += added;
                totalModified += modified;
                totalRemoved += removed;
            }

            return Results.Ok(new { added = totalAdded, modified = totalModified, removed = totalRemoved });
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
