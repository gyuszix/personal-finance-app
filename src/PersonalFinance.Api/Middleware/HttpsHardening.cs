using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace PersonalFinance.Api.Middleware;

// HTTPS-only behind a TLS-terminating proxy - Caddy in the compose prod
// override, the Container Apps ingress in Azure (#123).
//
// ReverseProxy:Enabled  trust X-Forwarded-For/-Proto from the proxy in
//                       front, so the app knows the request was HTTPS and
//                       who the real client is (rate limits, audit IPs).
//                       Only turn it on when the API is reachable through
//                       that proxy alone - otherwise anyone could spoof them.
// ReverseProxy:KnownProxies  optional proxy IPs to trust; empty trusts the
//                       one hop in front (Container Apps' ingress has no
//                       fixed address).
public static class HttpsHardening
{
    public static IServiceCollection AddHttpsHardening(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        // Read when the options are built, so configuration layered on after
        // registration (environment, WebApplicationFactory) counts.
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            var known = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var ip in known) options.KnownProxies.Add(IPAddress.Parse(ip));
        });

        services.AddHsts(options =>
        {
            // A year, the usual minimum for HSTS preload lists. Not
            // preloaded: that's a one-way commitment for the whole domain.
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
            options.Preload = false;
        });

        // Behind a proxy the public HTTPS port is 443 whatever the container
        // listens on; without this the redirect can't tell where to send people.
        services.AddOptions<Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            if (configuration.GetValue("ReverseProxy:Enabled", false)) options.HttpsPort = 443;
        });

        return services;
    }

    public static WebApplication UseHttpsHardening(this WebApplication app)
    {
        if (app.Configuration.GetValue("ReverseProxy:Enabled", false))
            app.UseForwardedHeaders();

        // Local development stays plain HTTP (the MAUI app talks to
        // http://localhost:5140); everywhere else is HTTPS-only.
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
            // Except the health probes: the container platform (Docker's
            // HEALTHCHECK, Container Apps' probes) calls them over plain HTTP
            // on the container's own port, and a redirect reads as a failure.
            app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"),
                branch => branch.UseHttpsRedirection());
        }

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers.XFrameOptions = "DENY";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            // The API serves JSON, never pages to render - except the
            // Scalar docs UI in Development, which needs its scripts.
            if (!context.Request.Path.StartsWithSegments("/scalar"))
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            await next();
        });

        return app;
    }
}
