using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using Going.Plaid;
using PersonalFinance.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using PersonalFinance.Api.Services;
using Asp.Versioning;

namespace PersonalFinance.Api.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        services.AddHttpContextAccessor();

        if (env.IsEnvironment("Testing"))
        {
            // An in-memory SQLite database only lives as long as at least one
            // connection to it stays open. EF Core opens and closes a connection
            // per operation, so if we handed it a connection *string* the DB (and
            // its schema) would be thrown away the moment EnsureCreated() finished.
            // Instead we open a single connection here and keep it alive for the
            // whole app lifetime, then hand that same connection to every DbContext.
            var connection = new SqliteConnection("DataSource=testdb;Mode=Memory;Cache=Shared");
            connection.Open();
            services.AddSingleton(connection);
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        }
        else
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(config.GetConnectionString("Default")));
        }

        return services;
    }

    public static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        services.AddIdentityApiEndpoints<User>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = config["Jwt:Issuer"],
                ValidAudience = config["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(config["Jwt:Key"]!))
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("ResourceOwner", policy => policy.Requirements.Add(new ResourceOwnerRequirement()));
        });

        services.AddScoped<IAuthorizationHandler, TransactionOwnerHandler>();

        return services;
    }

    public static IServiceCollection AddApiVersioningSetup(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1.0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        });

        return services;
    }

    public static IServiceCollection AddRateLimitingSetup(this IServiceCollection services, IWebHostEnvironment env)
    {
        // Integration tests hammer /auth/register and /auth/login in rapid
        // succession from the same test-host "IP" as normal, correct behavior -
        // that's not what this is meant to catch, so use effectively-unlimited
        // limits in Testing rather than fighting the test suite. The policies
        // still exist structurally (RequireRateLimiting("auth") needs the named
        // policy registered either way), just not restrictive.
        var isTesting = env.IsEnvironment("Testing");
        var authPermitLimit = isTesting ? int.MaxValue : 10;
        var globalPermitLimit = isTesting ? int.MaxValue : 200;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Stricter policy for auth endpoints (login/register) - guards
            // against credential brute-forcing and mass account creation.
            // Partitioned per client IP so one abusive caller doesn't affect others.
            options.AddPolicy("auth", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authPermitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // General-purpose limiter for everything else, also per client IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetClientIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = globalPermitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    private static string GetClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString();

    public static IServiceCollection AddPlaidIntegration(this IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<PlaidSyncService>();
        services.Configure<PlaidOptions>(config.GetSection("Plaid"));
        services.AddHttpClient();
        services.AddSingleton<PlaidClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PlaidOptions>>();
            return new PlaidClient(options);
        });
        services.AddDataProtection();
        services.AddSingleton<PlaidTokenProtector>();

        return services;
    }
}
