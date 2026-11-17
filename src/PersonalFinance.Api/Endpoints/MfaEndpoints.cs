using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Auth;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Api.Endpoints;

// TOTP (RFC 6238) multi-factor authentication on ASP.NET Identity's
// authenticator support (#117). Setting up is two steps - get a key, prove
// the authenticator app has it with a code - so a typo in the key can't
// lock anyone out.
public static class MfaEndpoints
{
    private const string Issuer = "PersonalFinance";
    private const int RecoveryCodeCount = 10;

    public static void MapMfaEndpoints(this IEndpointRouteBuilder app)
    {
        var mfa = app.MapGroup("/auth/mfa").RequireAuthorization().RequireRateLimiting("auth");

        mfa.MapGet("", async (UserManager<User> users, HttpContext http) =>
        {
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.Unauthorized();

            return Results.Ok(new MfaStatusResponse
            {
                Enabled = user.TwoFactorEnabled,
                RecoveryCodesLeft = user.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(user) : 0
            });
        });

        // A new secret each call; nothing changes for login until /enable.
        mfa.MapPost("/setup", async (UserManager<User> users, HttpContext http) =>
        {
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.Unauthorized();
            if (user.TwoFactorEnabled)
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Two-factor authentication is already on. Turn it off first to set up a new authenticator.");

            await users.ResetAuthenticatorKeyAsync(user);
            var key = (await users.GetAuthenticatorKeyAsync(user))!;

            return Results.Ok(new MfaSetupResponse
            {
                SharedKey = Group(key),
                AuthenticatorUri = AuthenticatorUri(user.Email!, key)
            });
        });

        mfa.MapPost("/enable", async (
            MfaCodeRequest request, UserManager<User> users, TotpVerifier totp, AuditLog audit, HttpContext http) =>
        {
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.Unauthorized();
            if (user.TwoFactorEnabled)
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Two-factor authentication is already on.");
            if (await users.GetAuthenticatorKeyAsync(user) is null)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Call /auth/mfa/setup first.");

            if (!await totp.VerifyAsync(user, request.Code))
            {
                await audit.RecordAsync(AuditEventTypes.MfaFailed, user.Id, new { reason = "wrong-code", during = "enable" });
                return InvalidCode();
            }

            await users.SetTwoFactorEnabledAsync(user, true);
            var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
            await audit.RecordAsync(AuditEventTypes.MfaEnabled, user.Id);

            // Shown once; Identity keeps only what it needs to check them.
            return Results.Ok(new MfaRecoveryCodesResponse { RecoveryCodes = codes!.ToList() });
        });

        // Needs a current code (or a recovery code): a stolen access token
        // alone mustn't be able to strip the second factor.
        mfa.MapPost("/disable", async (
            MfaCodeRequest request, UserManager<User> users, TotpVerifier totp, AuditLog audit, HttpContext http) =>
        {
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.Unauthorized();
            if (!user.TwoFactorEnabled) return Results.NoContent();

            if (!await VerifyCodeOrRecoveryCodeAsync(users, totp, user, request.Code))
            {
                await audit.RecordAsync(AuditEventTypes.MfaFailed, user.Id, new { reason = "wrong-code", during = "disable" });
                return InvalidCode();
            }

            await users.SetTwoFactorEnabledAsync(user, false);
            await users.ResetAuthenticatorKeyAsync(user); // the old secret is dead even if re-enabled later
            await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
            await audit.RecordAsync(AuditEventTypes.MfaDisabled, user.Id);
            return Results.NoContent();
        });

        mfa.MapPost("/recovery-codes", async (
            MfaCodeRequest request, UserManager<User> users, TotpVerifier totp, AuditLog audit, HttpContext http) =>
        {
            var user = await users.GetUserAsync(http.User);
            if (user is null) return Results.Unauthorized();
            if (!user.TwoFactorEnabled)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Two-factor authentication is off.");

            if (!await totp.VerifyAsync(user, request.Code))
            {
                await audit.RecordAsync(AuditEventTypes.MfaFailed, user.Id, new { reason = "wrong-code", during = "recovery-codes" });
                return InvalidCode();
            }

            var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
            await audit.RecordAsync(AuditEventTypes.MfaRecoveryCodesRegenerated, user.Id);
            return Results.Ok(new MfaRecoveryCodesResponse { RecoveryCodes = codes!.ToList() });
        });
    }

    private static async Task<bool> VerifyCodeOrRecoveryCodeAsync(
        UserManager<User> users, TotpVerifier totp, User user, string code)
    {
        if (TotpVerifier.LooksLikeTotp(code))
            return await totp.VerifyAsync(user, code);
        return (await users.RedeemTwoFactorRecoveryCodeAsync(user, TotpVerifier.NormalizeRecoveryCode(code))).Succeeded;
    }

    private static IResult InvalidCode() => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid two-factor code",
        extensions: new Dictionary<string, object?> { ["code"] = MfaProblemCodes.InvalidTwoFactorCode });

    private static string Group(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0) builder.Append(' ');
            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }
        return builder.ToString().ToLowerInvariant();
    }

    // Key URI format understood by Google Authenticator, 1Password, Authy etc.
    private static string AuthenticatorUri(string email, string key) =>
        $"otpauth://totp/{UrlEncoder.Default.Encode(Issuer)}:{UrlEncoder.Default.Encode(email)}" +
        $"?secret={key}&issuer={UrlEncoder.Default.Encode(Issuer)}&digits=6&period=30";
}
