using Microsoft.AspNetCore.Identity;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Insights;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Endpoints;

// The Dashboard's metrics, one endpoint per IInsight. Adding a metric is a
// new IInsight class plus one MapInsight line here.
public static class InsightEndpoints
{
    public static void MapInsightEndpoints(this IEndpointRouteBuilder app)
    {
        var insights = app.MapGroup("/insights").RequireAuthorization();

        insights.MapInsight<SpendingByCategoryInsight, SpendingByCategoryResponse>();
        insights.MapInsight<CashflowTrendInsight, CashflowTrendResponse>();
        insights.MapInsight<CreditDebitSplitInsight, CreditDebitSplitResponse>();
        insights.MapInsight<CreditShareTrendInsight, CreditShareTrendResponse>();
        insights.MapInsight<CreditDebitByCategoryInsight, CreditDebitByCategoryResponse>();
        insights.MapInsight<CardPayoffInsight, CardPayoffResponse>();
        insights.MapInsight<SavingsRateInsight, SavingsRateResponse>();
        insights.MapInsight<BurnRateInsight, BurnRateResponse>();
        insights.MapInsight<EmergencyFundInsight, EmergencyFundResponse>();
    }

    private static void MapInsight<TInsight, TResult>(this RouteGroupBuilder group)
        where TInsight : IInsight<TResult>
        where TResult : notnull
    {
        group.MapGet($"/{TInsight.Key}", async (
            string? month,
            HttpContext http,
            UserManager<User> userManager,
            SummaryCache cache,
            TimeProvider clock,
            ILogger<TInsight> logger,
            CancellationToken cancellationToken) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            if (!InsightMonth.TryParse(month, clock.GetUtcNow(), out var period, out var error))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["month"] = [error!] });

            var cacheKey = cache.InsightKey(userId, TInsight.Key, period.ToString());
            if (cache.TryGet<TResult>(cacheKey, out var cached))
                return Results.Ok(cached);

            var insight = ActivatorUtilities.CreateInstance<TInsight>(http.RequestServices);
            var result = await insight.CalculateAsync(period, cancellationToken);

            // IDs and the period only - never amounts.
            logger.LogInformation("Calculated insight {Insight} for user {UserId}, period {Period}",
                TInsight.Key, userId, period);

            cache.Set(cacheKey, result);
            return Results.Ok(result);
        });
    }
}
