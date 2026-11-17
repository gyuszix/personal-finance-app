using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Services.Auth;

namespace PersonalFinance.Api.Services.Encryption;

public record RewrapReport(int PlaidTokensRewrapped, int IdentityTokensRewrapped, int Failed, Dictionary<string, int> RemainingByKeyId);

// Key rotation, step 2 of 3 (docs/KEY_MANAGEMENT.md): moves every stored
// secret onto the current KEK - re-wrapping its data key, not re-encrypting
// it - and upgrades anything still in an older format. Idempotent: run it
// again and nothing changes. Run as `dotnet run --project src/PersonalFinance.Api -- rewrap-secrets`.
public class SecretRewrapper(AppDbContext db, PlaidTokenProtector plaidTokens, EnvelopeEncryptor envelope, ILogger<SecretRewrapper> logger)
{
    public async Task<RewrapReport> RewrapAllAsync(CancellationToken cancellationToken = default)
    {
        using var system = DbSystemScope.Enter(); // every user's secrets
        int plaid = 0, identity = 0, failed = 0;

        // Accounts of one Plaid Item share one ciphertext (the app groups
        // them by it), so re-wrap each distinct value once and give every
        // row the same result.
        var distinct = await db.Accounts.IgnoreQueryFilters()
            .Select(a => new { a.PlaidAccessToken, a.UserId })
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var token in distinct)
        {
            string upgraded;
            try
            {
                upgraded = plaidTokens.Upgrade(token.PlaidAccessToken, token.UserId);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                failed++;
                logger.LogError(ex, "Couldn't re-wrap a Plaid access token for user {UserId}; the bank needs re-linking", token.UserId);
                continue;
            }
            if (upgraded == token.PlaidAccessToken) continue;

            plaid += await db.Accounts.IgnoreQueryFilters()
                .Where(a => a.PlaidAccessToken == token.PlaidAccessToken && a.UserId == token.UserId)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.PlaidAccessToken, upgraded), cancellationToken);
        }

        var identityTokens = await db.UserTokens
            .Where(t => t.LoginProvider == ProtectedUserStore.IdentityLoginProvider
                        && ProtectedUserStore.ProtectedNames.Contains(t.Name) && t.Value != null)
            .ToListAsync(cancellationToken);
        foreach (var token in identityTokens)
        {
            try
            {
                var upgraded = EnvelopeEncryptor.IsEnvelope(token.Value!)
                    ? envelope.Rewrap(token.Value!)
                    : envelope.Encrypt(token.Value!, ProtectedUserStore.Context(token.UserId, token.Name));
                if (upgraded == token.Value) continue;
                token.Value = upgraded;
                identity++;
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                failed++;
                logger.LogError(ex, "Couldn't re-wrap Identity token {Name} for user {UserId}", token.Name, token.UserId);
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        var remaining = (await db.Accounts.IgnoreQueryFilters().Select(a => a.PlaidAccessToken).Distinct().ToListAsync(cancellationToken))
            .Concat(identityTokens.Select(t => t.Value!))
            .GroupBy(v => EnvelopeEncryptor.KeyIdOf(v) ?? "legacy")
            .ToDictionary(g => g.Key, g => g.Count());

        logger.LogInformation(
            "Re-wrapped {PlaidTokens} Plaid tokens and {IdentityTokens} Identity tokens ({Failed} failed); secrets by key: {Remaining}",
            plaid, identity, failed, remaining);
        return new RewrapReport(plaid, identity, failed, remaining);
    }
}
