namespace PersonalFinance.Api.Services.Insights;

// One Dashboard metric. Each lives in its own class and is served at
// GET /insights/{Key}?month=yyyy-MM by InsightEndpoints.MapInsight, which
// handles sign-in, month validation and caching. The class only calculates.
//
// Calculators are created per request with their constructor dependencies
// resolved from DI, and query through AppDbContext, whose per-user filter
// scopes every query to the signed-in user.
public interface IInsight<TResult>
{
    // URL segment, e.g. "spending-by-category".
    static abstract string Key { get; }

    Task<TResult> CalculateAsync(InsightMonth month, CancellationToken cancellationToken);
}
