using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using System.Text;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using Going.Plaid;
using PersonalFinance.Api.Authorization;
using Microsoft.AspNetCore.Authorization;

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

    public static IServiceCollection AddPlaidIntegration(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PlaidOptions>(config.GetSection("Plaid"));
        services.AddHttpClient();
        services.AddSingleton<PlaidClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PlaidOptions>>();
            return new PlaidClient(options);
        });

        return services;
    }
}