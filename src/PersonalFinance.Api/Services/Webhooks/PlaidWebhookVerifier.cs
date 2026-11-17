using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Going.Plaid;
using Going.Plaid.WebhookVerificationKey;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace PersonalFinance.Api.Services.Webhooks;

public enum WebhookVerification
{
    Valid,
    MissingHeader,
    Malformed,
    WrongAlgorithm,
    UnknownKey,
    ExpiredKey,
    BadSignature,
    Stale,
    BodyMismatch
}

// Checks the Plaid-Verification header Plaid signs every webhook with, as
// https://plaid.com/docs/api/webhooks/webhook-verification/ describes (#118):
// 1. it's a JWT signed ES256 - anything else (alg "none", HS256 with the
//    public key as secret...) is rejected before looking at a key;
// 2. its kid names a current Plaid key, fetched from
//    /webhook_verification_key/get and cached;
// 3. the signature verifies with that key;
// 4. it was issued in the last 5 minutes, so a captured webhook can't be
//    replayed later;
// 5. its request_body_sha256 matches the body we actually received, so the
//    signature covers the payload, not just the header.
public class PlaidWebhookVerifier(PlaidClient plaid, IMemoryCache cache, TimeProvider time, ILogger<PlaidWebhookVerifier> logger)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KeyCacheLifetime = TimeSpan.FromHours(24);

    public async Task<WebhookVerification> VerifyAsync(string? header, byte[] body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(header)) return WebhookVerification.MissingHeader;

        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken unverified;
        try
        {
            unverified = handler.ReadJwtToken(header);
        }
        catch (ArgumentException)
        {
            return WebhookVerification.Malformed;
        }

        if (unverified.Header.Alg != SecurityAlgorithms.EcdsaSha256) return WebhookVerification.WrongAlgorithm;
        if (string.IsNullOrEmpty(unverified.Header.Kid)) return WebhookVerification.Malformed;

        var (key, expired) = await GetKeyAsync(unverified.Header.Kid, cancellationToken);
        if (key is null) return WebhookVerification.UnknownKey;
        if (expired) return WebhookVerification.ExpiredKey;

        JwtSecurityToken verified;
        try
        {
            handler.ValidateToken(header, new TokenValidationParameters
            {
                IssuerSigningKey = key,
                ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                ValidateIssuerSigningKey = true,
                // Plaid's token carries iat, not iss/aud/exp; age is checked below.
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                RequireExpirationTime = false,
                RequireSignedTokens = true
            }, out var token);
            verified = (JwtSecurityToken)token;
        }
        catch (SecurityTokenException)
        {
            return WebhookVerification.BadSignature;
        }

        if (!long.TryParse(verified.Payload.TryGetValue("iat", out var iat) ? iat?.ToString() : null, out var issuedAt))
            return WebhookVerification.Malformed;
        var age = time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(issuedAt);
        // A little tolerance for clock drift the other way.
        if (age > MaxAge || age < -TimeSpan.FromMinutes(1)) return WebhookVerification.Stale;

        if (verified.Payload.TryGetValue("request_body_sha256", out var claimed) is false
            || claimed?.ToString() is not { Length: 64 } claimedHex)
            return WebhookVerification.Malformed;

        byte[] claimedHash;
        try
        {
            claimedHash = Convert.FromHexString(claimedHex);
        }
        catch (FormatException)
        {
            return WebhookVerification.Malformed;
        }

        return CryptographicOperations.FixedTimeEquals(claimedHash, SHA256.HashData(body))
            ? WebhookVerification.Valid
            : WebhookVerification.BodyMismatch;
    }

    private async Task<(SecurityKey? Key, bool Expired)> GetKeyAsync(string kid, CancellationToken cancellationToken)
    {
        var cacheKey = $"plaid-webhook-key:{kid}";
        if (cache.TryGetValue(cacheKey, out (SecurityKey Key, long? ExpiredAt) cached))
            return (cached.Key, IsExpired(cached.ExpiredAt));

        var response = await plaid.WebhookVerificationKeyGetAsync(new WebhookVerificationKeyGetRequest { KeyId = kid });
        if (response.Error is not null || response.Key is not { } jwk)
        {
            logger.LogWarning("Plaid has no webhook verification key {KeyId}: {ErrorCode}", kid, response.Error?.ErrorCode);
            return (null, false);
        }
        if (jwk.Kty != "EC" || jwk.Crv != "P-256" || jwk.Alg != "ES256")
        {
            logger.LogWarning("Ignoring Plaid webhook key {KeyId} with unexpected type {Kty}/{Crv}/{Alg}", kid, jwk.Kty, jwk.Crv, jwk.Alg);
            return (null, false);
        }

        var key = new ECDsaSecurityKey(ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64UrlEncoder.DecodeBytes(jwk.X), Y = Base64UrlEncoder.DecodeBytes(jwk.Y) }
        })) { KeyId = kid };

        long? expiredAt = jwk.ExpiredAt is { } e ? (long)e : null;
        cache.Set(cacheKey, (key as SecurityKey, expiredAt), KeyCacheLifetime);
        return (key, IsExpired(expiredAt));
    }

    // Plaid sets expired_at once a key has been rotated out.
    private bool IsExpired(long? expiredAt) =>
        expiredAt is { } at && DateTimeOffset.FromUnixTimeSeconds(at) <= time.GetUtcNow();
}
