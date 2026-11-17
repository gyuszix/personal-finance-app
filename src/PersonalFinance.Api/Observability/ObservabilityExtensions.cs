using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace PersonalFinance.Api.Observability;

// Structured logs (Serilog) and traces + metrics (OpenTelemetry) (#121).
//
// - Console: readable lines with the trace id in Development, compact JSON
//   (one object per line, trace/span ids included) everywhere else - what
//   a log pipeline ingests.
// - OTLP: when OTEL_EXPORTER_OTLP_ENDPOINT is set, logs, traces and metrics
//   go there too - the Aspire dashboard locally (docker compose), any
//   OpenTelemetry backend in production. Unset, nothing is exported.
//
// Secrets never make it in: request logs carry the path, not the query
// string (SignalR sends ?access_token= there); spans drop url.query; nothing
// logs headers or bodies.
public static class ObservabilityExtensions
{
    public const string ServiceName = "personalfinance-api";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var version = typeof(Telemetry).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var isDevelopment = builder.Environment.IsDevelopment();
        // One id for this process in both pipelines, so logs and traces show
        // up as the same resource (the container id, in a container).
        var instanceId = $"{Environment.MachineName}-{Environment.ProcessId}";

        builder.Services.AddSerilog((services, logger) =>
        {
            logger
                .ReadFrom.Configuration(builder.Configuration)
                .MinimumLevel.Information()
                // Framework chatter stays out; our own logs and request
                // summaries stay in. Hosting's "Request starting ..." would
                // also print full URLs, query strings included.
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .MinimumLevel.Override("Serilog.AspNetCore.RequestLoggingMiddleware", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service.name", ServiceName)
                .Enrich.WithProperty("deployment.environment", builder.Environment.EnvironmentName);

            if (isDevelopment)
                logger.WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {TraceId}{NewLine}{Exception}");
            else
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                logger.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint;
                    options.Protocol = OtlpProtocol.Grpc;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = ServiceName,
                        ["service.version"] = version,
                        ["service.instance.id"] = instanceId
                    };
                });
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(ServiceName, serviceVersion: version, autoGenerateServiceInstanceId: false, serviceInstanceId: instanceId)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.Name)
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Health probes would be most of the traces and none of the interest.
                    options.Filter = http => !http.Request.Path.StartsWithSegments("/health");
                    options.EnrichWithHttpRequest = (activity, _) => activity.SetTag("url.query", null);
                    options.RecordException = true;
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.EnrichWithHttpRequestMessage = (activity, _) => activity.SetTag("url.query", null);
                    options.RecordException = true;
                })
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddMeter(Telemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            otel.UseOtlpExporter();

        return builder;
    }

    // One summary line per request: method, path (no query string), status,
    // elapsed, and who - the user id, never their email.
    public static WebApplication UseRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod:l} {RequestPath:l} responded {StatusCode} in {Elapsed:0.0} ms";
            options.EnrichDiagnosticContext = (diagnostics, http) =>
            {
                diagnostics.Set("UserId", http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
                diagnostics.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString());
            };
            // Health probes at Verbose so they don't drown everything else.
            options.GetLevel = (http, _, ex) =>
                ex is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Error
                : http.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
                : LogEventLevel.Information;
        });
        return app;
    }
}
