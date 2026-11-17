using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using PersonalFinance.Api.DTOs;
using PersonalFinance.Api.Entities;
using Microsoft.Extensions.Options;
using PersonalFinance.Api.Services.Auth;
using PersonalFinance.Api.Middleware;

namespace PersonalFinance.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/register", async (
            RegisterRequest request,
            UserManager<User> userManager) =>
        {
            var user = new User { UserName = request.Email, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, "User");
                return Results.Ok("User registered successfully");
            }

            return Results.BadRequest(result.Errors);
        })
        .AddEndpointFilter<ValidationFilter<RegisterRequest>>()
        .RequireRateLimiting("auth");

        // ?useCookies=true is for browser clients: the refresh token is set
        // as an HttpOnly cookie and left out of the body. Native clients
        // omit it and get the token in the body for their keychain (#115).
        app.MapPost("/auth/login", async (
            LoginRequest request,
            bool? useCookies,
            UserManager<User> userManager,
            TokenService tokens,
            IOptions<JwtOptions> jwt,
            HttpContext http) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
                return Results.Unauthorized();

            var issued = await tokens.IssueAsync(user);
            return TokenResult(issued, useCookies == true, jwt.Value, http);
        })
        .RequireRateLimiting("auth");

        // Rotates the refresh token: the one presented is spent, a new one
        // is issued. From the body if sent there, otherwise the cookie - and
        // the new one goes back the same way it came.
        app.MapPost("/auth/refresh", async (
            RefreshRequest? request,
            TokenService tokens,
            IOptions<JwtOptions> jwt,
            HttpContext http) =>
        {
            var fromCookie = string.IsNullOrEmpty(request?.RefreshToken);
            var presented = fromCookie ? RefreshTokenCookie.Read(http.Request) : request!.RefreshToken;
            if (presented is null) return Results.Unauthorized();

            var result = await tokens.RefreshAsync(presented);
            if (result.Status != RefreshStatus.Ok)
            {
                if (fromCookie) RefreshTokenCookie.Clear(http.Response);
                return Results.Unauthorized();
            }

            return TokenResult(result.Tokens!, fromCookie, jwt.Value, http);
        })
        .RequireRateLimiting("auth");

        // Logout: ends this session (its whole token family). Other devices
        // stay signed in.
        app.MapPost("/auth/revoke", async (
            RefreshRequest? request,
            TokenService tokens,
            HttpContext http) =>
        {
            var fromCookie = string.IsNullOrEmpty(request?.RefreshToken);
            var presented = fromCookie ? RefreshTokenCookie.Read(http.Request) : request!.RefreshToken;
            if (fromCookie) RefreshTokenCookie.Clear(http.Response);
            if (presented is null || await tokens.RevokeAsync(presented) is null)
                return Results.BadRequest("Invalid token");

            return Results.NoContent();
        });
    }

    private static IResult TokenResult(IssuedTokens issued, bool useCookie, JwtOptions jwt, HttpContext http)
    {
        if (useCookie) RefreshTokenCookie.Set(http.Response, issued.RefreshToken, issued.RefreshTokenExpiresAt);

        return Results.Ok(new
        {
            token = issued.AccessToken,
            refreshToken = useCookie ? null : issued.RefreshToken,
            expiresIn = jwt.AccessTokenMinutes * 60
        });
    }
}
