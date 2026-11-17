using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Services;
using PersonalFinance.Shared;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Api.Endpoints;

// CRUD for categorisation rules (#124). Every change re-applies the user's
// rules to all their transactions, so insights and budgets reflect it at
// once (their caches are invalidated too).
public static class CategoryRuleEndpoints
{
    public const int MaxRulesPerUser = 200;

    public static void MapCategoryRuleEndpoints(this IEndpointRouteBuilder app)
    {
        var rules = app.MapGroup("/rules").RequireAuthorization();

        rules.MapGet("", async (AppDbContext db) => Results.Ok(await ResponsesAsync(db)));

        rules.MapPost("", async (CategoryRuleRequest request, AppDbContext db, UserManager<User> users,
            SummaryCache cache, HttpContext http, ILogger<Program> logger) =>
        {
            var userId = users.GetUserId(http.User)!;
            if (await ValidateAsync(request, db) is { } problem) return problem;
            if (await db.CategoryRules.CountAsync() >= MaxRulesPerUser)
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: $"At most {MaxRulesPerUser} rules.");

            var rule = new CategoryRule { UserId = userId, CreatedAt = DateTime.UtcNow };
            Copy(request, rule);
            db.CategoryRules.Add(rule);
            await db.SaveChangesAsync();
            var changed = await CategoryRules.ReapplyAsync(db, userId);
            cache.InvalidateForUser(userId);

            logger.LogInformation("Rule {RuleId} created for user {UserId}; {Changed} transactions recategorised", rule.CategoryRuleId, userId, changed);
            return Results.Created($"/api/v1/rules/{rule.CategoryRuleId}",
                (await ResponsesAsync(db)).Single(r => r.Id == rule.CategoryRuleId));
        });

        rules.MapPut("/{id:int}", async (int id, CategoryRuleRequest request, AppDbContext db,
            SummaryCache cache, ILogger<Program> logger) =>
        {
            var rule = await db.CategoryRules.FirstOrDefaultAsync(r => r.CategoryRuleId == id);
            if (rule is null) return Results.NotFound();
            if (await ValidateAsync(request, db) is { } problem) return problem;

            Copy(request, rule);
            await db.SaveChangesAsync();
            var changed = await CategoryRules.ReapplyAsync(db, rule.UserId);
            cache.InvalidateForUser(rule.UserId);

            logger.LogInformation("Rule {RuleId} updated; {Changed} transactions recategorised", id, changed);
            return Results.Ok((await ResponsesAsync(db)).Single(r => r.Id == id));
        });

        rules.MapDelete("/{id:int}", async (int id, AppDbContext db, SummaryCache cache, ILogger<Program> logger) =>
        {
            var rule = await db.CategoryRules.FirstOrDefaultAsync(r => r.CategoryRuleId == id);
            if (rule is null) return Results.NotFound();

            db.CategoryRules.Remove(rule);
            await db.SaveChangesAsync();
            // Its transactions fall back to the next matching rule, or Plaid's category.
            var changed = await CategoryRules.ReapplyAsync(db, rule.UserId);
            cache.InvalidateForUser(rule.UserId);

            logger.LogInformation("Rule {RuleId} deleted; {Changed} transactions recategorised", id, changed);
            return Results.NoContent();
        });
    }

    private static void Copy(CategoryRuleRequest request, CategoryRule rule)
    {
        rule.Pattern = request.Pattern.Trim();
        rule.MatchType = request.MatchType;
        rule.Category = request.Category;
        rule.AccountId = request.AccountId;
        rule.MinAmount = request.MinAmount;
        rule.MaxAmount = request.MaxAmount;
        rule.Priority = request.Priority;
    }

    private static async Task<IResult?> ValidateAsync(CategoryRuleRequest request, AppDbContext db)
    {
        var errors = new Dictionary<string, string[]>();
        var pattern = request.Pattern?.Trim() ?? "";
        if (pattern.Length is < 2 or > 128) errors["pattern"] = ["Pattern must be 2-128 characters."];
        if (!RuleMatchTypes.All.Contains(request.MatchType)) errors["matchType"] = [$"Match type must be one of: {string.Join(", ", RuleMatchTypes.All)}."];
        if (!PlaidCategories.Primary.Contains(request.Category)) errors["category"] = ["Category must be one of Plaid's primary categories."];
        if (request.MinAmount > request.MaxAmount) errors["minAmount"] = ["Minimum can't be above the maximum."];
        if (request.Priority is < 0 or > 1000) errors["priority"] = ["Priority must be 0-1000."];
        // The query filter (and RLS) make another user's account not found.
        if (request.AccountId is { } accountId && !await db.Accounts.AnyAsync(a => a.AccountId == accountId))
            errors["accountId"] = ["No such account."];
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static async Task<List<CategoryRuleResponse>> ResponsesAsync(AppDbContext db)
    {
        var counts = await db.Transactions.Where(t => t.CategoryRuleId != null)
            .GroupBy(t => t.CategoryRuleId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Id, g => g.Count);

        return (await db.CategoryRules.ToListAsync())
            .OrderBy(r => r.Priority).ThenBy(r => r.CategoryRuleId)
            .Select(r => new CategoryRuleResponse
            {
                Id = r.CategoryRuleId,
                Pattern = r.Pattern,
                MatchType = r.MatchType,
                Category = r.Category,
                AccountId = r.AccountId,
                MinAmount = r.MinAmount,
                MaxAmount = r.MaxAmount,
                Priority = r.Priority,
                MatchedTransactions = counts.GetValueOrDefault(r.CategoryRuleId)
            }).ToList();
    }
}
