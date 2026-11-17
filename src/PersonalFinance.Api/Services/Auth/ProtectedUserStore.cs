using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Services.Encryption;

namespace PersonalFinance.Api.Services.Auth;

// Identity keeps each user's TOTP secret and recovery codes in
// AspNetUserTokens as plain text - enough, with a database copy, to generate
// valid codes. This store envelope-encrypts those two tokens on the way in
// and decrypts them on the way out, bound to the user (#120, from #117).
// Values written before this (plain text) are still read, and re-encrypted
// on the next write or by `rewrap-secrets`.
public class ProtectedUserStore(AppDbContext context, EnvelopeEncryptor envelope, IdentityErrorDescriber? describer = null)
    : UserStore<User, IdentityRole, AppDbContext>(context, describer)
{
    // Identity's own names for them (UserStoreBase).
    public const string IdentityLoginProvider = "[AspNetUserStore]";
    public static readonly string[] ProtectedNames = ["AuthenticatorKey", "RecoveryCodes"];

    public static bool IsProtected(string loginProvider, string name) =>
        loginProvider == IdentityLoginProvider && ProtectedNames.Contains(name);

    public static string Context(string userId, string name) => $"identity-token:{name}:{userId}";

    public override Task SetTokenAsync(User user, string loginProvider, string name, string? value, CancellationToken cancellationToken)
    {
        if (value is not null && IsProtected(loginProvider, name))
            value = envelope.Encrypt(value, Context(user.Id, name));
        return base.SetTokenAsync(user, loginProvider, name, value, cancellationToken);
    }

    public override async Task<string?> GetTokenAsync(User user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        var value = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        return value is not null && IsProtected(loginProvider, name) && EnvelopeEncryptor.IsEnvelope(value)
            ? envelope.Decrypt(value, Context(user.Id, name))
            : value;
    }
}
