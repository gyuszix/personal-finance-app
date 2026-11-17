using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using PersonalFinance.Api.DTOs;
using PersonalFinance.Api.Entities;
using Microsoft.Extensions.Options;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Auth;
using PersonalFinance.Shared.DTOs;
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
            TotpVerifier totp,
            AuditLog audit,
            IOptions<JwtOptions> jwt,
            HttpContext http) =>
        {
            async Task<IResult> SecondFactorFailedAsync(User user, string reason)
            {
                await userManager.AccessFailedAsync(user);
                await audit.RecordAsync(AuditEventTypes.MfaFailed, user.Id, new { reason });
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Invalid two-factor code",
                    extensions: new Dictionary<string, object?> { ["code"] = MfaProblemCodes.InvalidTwoFactorCode });
            }

            var user = await userManager.FindByEmailAsync(request.Email);
            if (user == null)
            {
                // No user to attach it to, and the email isn't recorded: it's
                // whatever the caller typed, possibly someone's password.
                await audit.RecordAsync(AuditEventTypes.LoginFailed, null, new { reason = "unknown-email" });
                return Results.Unauthorized();
            }

            // Wrong passwords and wrong second-factor codes both count toward
            // the lockout (5 tries, then 15 minutes - see AddIdentityServices),
            // which is what makes a 6-digit code impractical to guess (#117).
            if (await userManager.IsLockedOutAsync(user))
            {
                await audit.RecordAsync(AuditEventTypes.LoginFailed, user.Id, new { reason = "locked-out" });
                return Results.Unauthorized();
            }

            if (!await userManager.CheckPasswordAsync(user, request.Password))
            {
                await userManager.AccessFailedAsync(user);
                await audit.RecordAsync(AuditEventTypes.LoginFailed, user.Id, new { reason = "wrong-password" });
                return Results.Unauthorized();
            }

            if (user.TwoFactorEnabled)
            {
                if (!string.IsNullOrWhiteSpace(request.TwoFactorCode))
                {
                    if (!await totp.VerifyAsync(user, request.TwoFactorCode))
                        return await SecondFactorFailedAsync(user, "wrong-code");
                }
                else if (!string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode))
                {
                    var redeemed = await userManager.RedeemTwoFactorRecoveryCodeAsync(
                        user, TotpVerifier.NormalizeRecoveryCode(request.TwoFactorRecoveryCode));
                    if (!redeemed.Succeeded)
                        return await SecondFactorFailedAsync(user, "wrong-recovery-code");

                    await audit.RecordAsync(AuditEventTypes.MfaRecoveryCodeUsed, user.Id,
                        new { recoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user) });
                }
                else
                {
                    // Password was right; not a failure, just the next step.
                    return Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Two-factor code required",
                        detail: "Send the login again with twoFactorCode or twoFactorRecoveryCode.",
                        extensions: new Dictionary<string, object?> { ["code"] = MfaProblemCodes.RequiresTwoFactor });
                }
            }

            await userManager.ResetAccessFailedCountAsync(user);

            var issued = await tokens.IssueAsync(user);
            await audit.RecordAsync(AuditEventTypes.LoginSucceeded, user.Id,
                new { sessionId = issued.FamilyId, cookie = useCookies == true });
            return TokenResult(issued, useCookies == true, jwt.Value, http);
        })
        .RequireRateLimiting("auth");

        // Rotates the refresh token: the one presented is spent, a new one
        // is issued. From the body if sent there, otherwise the cookie - and
        // the new one goes back the same way it came.
        app.MapPost("/auth/refresh", async (
            RefreshRequest? request,
            TokenService tokens,
            AuditLog audit,
            IOptions<JwtOptions> jwt,
            HttpContext http) =>
        {
            var fromCookie = string.IsNullOrEmpty(request?.RefreshToken);
            var presented = fromCookie ? RefreshTokenCookie.Read(http.Request) : request!.RefreshToken;
            if (presented is null) return Results.Unauthorized();

            var result = await tokens.RefreshAsync(presented);
            if (result.Status == RefreshStatus.ReuseDetected)
                await audit.RecordAsync(AuditEventTypes.RefreshTokenReuseDetected, result.UserId,
                    new { sessionId = result.FamilyId, sessionRevoked = true });
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
            AuditLog audit,
            HttpContext http) =>
        {
            var fromCookie = string.IsNullOrEmpty(request?.RefreshToken);
            var presented = fromCookie ? RefreshTokenCookie.Read(http.Request) : request!.RefreshToken;
            if (fromCookie) RefreshTokenCookie.Clear(http.Response);
            var revoked = presented is null ? null : await tokens.RevokeAsync(presented);
            if (revoked is null) return Results.BadRequest("Invalid token");

            await audit.RecordAsync(AuditEventTypes.Logout, revoked.Value.UserId, new { sessionId = revoked.Value.FamilyId });

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
