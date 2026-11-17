using Microsoft.AspNetCore.DataProtection;
using PersonalFinance.Api.Services.Encryption;

namespace PersonalFinance.Api.Services;

// Plaid access tokens are never stored in plaintext (#120): envelope
// encryption with a per-token data key, the key-encryption key held outside
// the database, and the owning user bound in as associated data - a token
// copied into another user's row won't decrypt there.
//
// Tokens stored before #120 were encrypted with ASP.NET Data Protection
// ("CfDJ8..."). They still decrypt, and `rewrap-secrets` upgrades them.
public class PlaidTokenProtector(EnvelopeEncryptor envelope, IDataProtectionProvider dataProtection)
{
    // The Data Protection purpose the old tokens were protected under.
    public const string LegacyPurpose = "PlaidAccessToken";

    private readonly IDataProtector _legacy = dataProtection.CreateProtector(LegacyPurpose);

    public string Protect(string accessToken, string userId) => envelope.Encrypt(accessToken, Context(userId));

    // Throws CryptographicException if the value doesn't decrypt (wrong
    // user, a retired key, a lost Data Protection key ring, tampering).
    public string Unprotect(string protectedAccessToken, string userId) =>
        EnvelopeEncryptor.IsEnvelope(protectedAccessToken)
            ? envelope.Decrypt(protectedAccessToken, Context(userId))
            : _legacy.Unprotect(protectedAccessToken);

    // Current format under the current key: re-wraps envelope values, and
    // re-encrypts legacy Data Protection ones.
    public string Upgrade(string protectedAccessToken, string userId) =>
        EnvelopeEncryptor.IsEnvelope(protectedAccessToken)
            ? envelope.Rewrap(protectedAccessToken)
            : Protect(_legacy.Unprotect(protectedAccessToken), userId);

    private static string Context(string userId) => $"plaid-access-token:{userId}";
}
