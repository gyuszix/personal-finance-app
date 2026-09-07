using System.Globalization;
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

        // Spend by category for a given month (defaults to the current month).
        // Excludes pending transactions - they can still change amount/category
        // before they post, so including them would make totals unstable.
        app.MapGet("/transactions/summary", async (
            string? month,
            AppDbContext db,
            UserManager<User> userManager,
            HttpContext http,
            ILogger<Program> logger) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            if (!TryResolveMonthPeriod(month, out var periodStart))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["month"] = ["month must be in yyyy-MM format, e.g. 2026-09"]
                });
            }

            var periodEnd = periodStart.AddMonths(1);

            var summary = await db.Transactions
                .Where(t => !t.IsPending && t.Date >= periodStart && t.Date < periodEnd)
                .GroupBy(t => t.CategoryPrimary ?? "Uncategorized")
                .Select(g => new TransactionSummaryResponse
                {
                    Category = g.Key,
                    Total = g.Sum(t => t.Amount),
                    TransactionCount = g.Count()
                })
                .OrderByDescending(s => s.Total)
                .ToListAsync();

            logger.LogInformation(
                "Returned spend summary for user {UserId}, period {PeriodStart:yyyy-MM}, {CategoryCount} categories",
                userId, periodStart, summary.Count);

            return Results.Ok(summary);
        }).RequireAuthorization();

        // Income vs. expense for a given month (same default/format as
        // /transactions/summary). Excludes pending transactions (same reason
        // as above) and transfer categories - TRANSFER_IN/TRANSFER_OUT are
        // money moving between the user's own linked accounts, not real
        // income or spending, and including them would double-count and
        // inflate both sides of the cash flow.
        app.MapGet("/transactions/cashflow", async (
            string? month,
            AppDbContext db,
            UserManager<User> userManager,
            HttpContext http,
            ILogger<Program> logger) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            if (!TryResolveMonthPeriod(month, out var periodStart))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["month"] = ["month must be in yyyy-MM format, e.g. 2026-09"]
                });
            }

            var periodEnd = periodStart.AddMonths(1);

            // Plaid convention: positive Amount = money out (expense),
            // negative Amount = money in (income) - see /transactions/summary
            // for the same convention showing up in per-category totals.
            var cashflowTransactions = db.Transactions.Where(t =>
                !t.IsPending
                && t.Date >= periodStart && t.Date < periodEnd
                && t.CategoryPrimary != "TRANSFER_IN"
                && t.CategoryPrimary != "TRANSFER_OUT");

            var income = await cashflowTransactions
                .Where(t => t.Amount < 0)
                .SumAsync(t => -t.Amount);

            var expenses = await cashflowTransactions
                .Where(t => t.Amount >= 0)
                .SumAsync(t => t.Amount);

            logger.LogInformation(
                "Returned cash flow for user {UserId}, period {PeriodStart:yyyy-MM}: income {Income}, expenses {Expenses}",
                userId, periodStart, income, expenses);

            return Results.Ok(new CashflowResponse
            {
                Income = income,
                Expenses = expenses,
                Net = income - expenses
            });
        }).RequireAuthorization();

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

    // Shared by /transactions/summary and /transactions/cashflow - resolves
    // a "yyyy-MM" query param to the first instant of that month (UTC),
    // defaulting to the current month when omitted. Returns false (instead
    // of throwing) on an unparseable month so callers can turn it into a
    // 400 ValidationProblem.
    private static bool TryResolveMonthPeriod(string? month, out DateTime periodStart)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            var now = DateTime.UtcNow;
            periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParseExact(
            month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            periodStart = DateTime.SpecifyKind(new DateTime(parsed.Year, parsed.Month, 1), DateTimeKind.Utc);
            return true;
        }

        periodStart = default;
        return false;
    }
}
