using Microsoft.AspNetCore.Identity;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Services.Auth;

// Wraps Identity's authenticator (RFC 6238 TOTP) check with replay
// protection: Identity accepts a code for its whole ~90 s validity window,
// so a code seen over someone's shoulder (or phished in real time) could
// be used twice. The last accepted code is remembered, hashed, and refused
// until it can no longer be valid (#117).
public class TotpVerifier(UserManager<User> userManager, TimeProvider time)
{
    private const string LoginProvider = "[PersonalFinance:Totp]";
    private const string LastUsedName = "LastUsed";
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(3);

    public async Task<bool> VerifyAsync(User user, string code)
    {
        code = Normalize(code);
        if (code.Length != 6 || !code.All(char.IsAsciiDigit)) return false;

        var last = await userManager.GetAuthenticationTokenAsync(user, LoginProvider, LastUsedName);
        var hash = TokenService.Hash($"{user.Id}:{code}");
        var now = time.GetUtcNow();
        if (last?.Split('|') is [var lastHash, var lastAt]
            && lastHash == hash
            && long.TryParse(lastAt, out var lastUnix)
            && now - DateTimeOffset.FromUnixTimeSeconds(lastUnix) < ReplayWindow)
            return false;

        if (!await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
            return false;

        await userManager.SetAuthenticationTokenAsync(user, LoginProvider, LastUsedName, $"{hash}|{now.ToUnixTimeSeconds()}");
        return true;
    }

    // "123 456" / "123-456" as some authenticator apps display it.
    public static string Normalize(string code) => code.Replace(" ", "").Replace("-", "").Trim();

    // Recovery codes are compared exactly as Identity issued them
    // ("XXXXX-XXXXX"), so keep the dash; forgive spacing and case.
    public static string NormalizeRecoveryCode(string code) => code.Replace(" ", "").Trim().ToUpperInvariant();

    public static bool LooksLikeTotp(string code) => Normalize(code) is { Length: 6 } c && c.All(char.IsAsciiDigit);
}
