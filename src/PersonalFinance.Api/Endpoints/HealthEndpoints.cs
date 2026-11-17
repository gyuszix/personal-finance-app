using Microsoft.Extensions.Diagnostics.HealthChecks;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Endpoints;

// Probes for the container platform (#122):
//   /health/live  - the process is up and serving; restart it if not
//   /health/ready - it can do its job (the database answers); route traffic to it
// Anonymous, unversioned, not rate limited, and no detail in the response.
public static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddHealthChecksSetup(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag]);
        return services;
    }

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new() { Predicate = _ => false })
            .AllowAnonymous().DisableRateLimiting();
        app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains(ReadyTag) })
            .AllowAnonymous().DisableRateLimiting();
    }

    private sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database unreachable");
    }
}
