using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.DTOs;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Middleware;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Insights;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Api.Endpoints;

// Monthly limits per category (#88). A category with no saved limit uses a
// suggestion from recent spending until the user sets one.
public static class BudgetEndpoints
{
    public static void MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var budgets = app.MapGroup("/budgets").RequireAuthorization();

        // Saved and suggested limits for every category with either.
        budgets.MapGet("/", async (AppDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            var now = clock.GetUtcNow();
            var lines = await Budgets.ForAsync(db, InsightMonth.Containing(now), now, cancellationToken);
            return Results.Ok(lines.Select(l => new BudgetResponse
            {
                Category = l.Category,
                MonthlyLimit = l.Limit,
                SuggestedLimit = l.Suggested,
                IsSuggested = l.IsSuggested
            }).ToList());
        });

        budgets.MapPut("/{category}", async (
            string category,
            SetBudgetRequest request,
            AppDbContext db,
            UserManager<User> userManager,
            SummaryCache cache,
            HttpContext http,
            ILogger<Program> logger) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();
            if (!Budgets.Categories.Contains(category)) return UnknownCategory();

            var limit = Math.Round(request.MonthlyLimit!.Value, 2, MidpointRounding.AwayFromZero);
            var budget = await db.Budgets.FirstOrDefaultAsync(b => b.Category == category);
            if (budget == null)
                db.Budgets.Add(new Budget { UserId = userId, Category = category, MonthlyLimit = limit });
            else
                budget.MonthlyLimit = limit;
            await db.SaveChangesAsync();

            cache.InvalidateForUser(userId);
            logger.LogInformation("Saved budget for user {UserId}, category {Category}", userId, category);
            return Results.NoContent();
        }).AddEndpointFilter<ValidationFilter<SetBudgetRequest>>();

        // Back to the suggestion.
        budgets.MapDelete("/{category}", async (
            string category,
            AppDbContext db,
            UserManager<User> userManager,
            SummaryCache cache,
            HttpContext http) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();
            if (!Budgets.Categories.Contains(category)) return UnknownCategory();

            await db.Budgets.Where(b => b.Category == category).ExecuteDeleteAsync();
            cache.InvalidateForUser(userId);
            return Results.NoContent();
        });
    }

    private static IResult UnknownCategory() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["category"] = [$"category must be one of: {string.Join(", ", Budgets.Categories.Order())}"]
    });
}
