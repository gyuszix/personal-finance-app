using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Services.Auth;

public record IssuedTokens(string AccessToken, string RefreshToken, DateTime RefreshTokenExpiresAt, Guid FamilyId);

public enum RefreshStatus { Ok, Invalid, Expired, ReuseDetected }

public record RefreshResult(RefreshStatus Status, IssuedTokens? Tokens = null, string? UserId = null, Guid? FamilyId = null);

// Issues, rotates and revokes tokens. Follows the OAuth refresh-token
// guidance (RFC 9700 §4.14): short-lived access tokens, refresh tokens that
// rotate on every use, stored hashed, and reuse of a rotated token treated as
// theft - revoking that token's family, not every session the user has (#115).
public class TokenService(
    AppDbContext db,
    UserManager<User> userManager,
    IOptions<JwtOptions> options,
    TimeProvider time)
{
    private JwtOptions Options => options.Value;
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // A new family: one login.
    public async Task<IssuedTokens> IssueAsync(User user)
    {
        var now = Now;
        var (raw, token) = NewRefreshToken(user.Id, Guid.NewGuid(), familyStartedAt: now);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return new IssuedTokens(await AccessTokenAsync(user), raw, token.ExpiresAt, token.FamilyId);
    }

    public async Task<RefreshResult> RefreshAsync(string rawRefreshToken)
    {
        var existing = await FindAsync(rawRefreshToken);
        if (existing is null) return new RefreshResult(RefreshStatus.Invalid);

        if (existing.IsRevoked)
        {
            // Presenting a token that was already rotated means two parties
            // hold this session - the legitimate client and whoever copied
            // the token. We can't tell which is which, so end the session.
            // (A token revoked by logout is just dead, not evidence of theft.)
            if (existing.RevokedReason != RefreshTokenRevocation.Rotated)
                return new RefreshResult(RefreshStatus.Invalid);

            await RevokeFamilyAsync(existing.FamilyId, RefreshTokenRevocation.ReuseDetected);
            return new RefreshResult(RefreshStatus.ReuseDetected, UserId: existing.UserId, FamilyId: existing.FamilyId);
        }

        if (existing.ExpiresAt <= Now) return new RefreshResult(RefreshStatus.Expired, UserId: existing.UserId);

        var user = await userManager.FindByIdAsync(existing.UserId);
        if (user is null) return new RefreshResult(RefreshStatus.Invalid);

        var (raw, next) = NewRefreshToken(user.Id, existing.FamilyId, existing.FamilyStartedAt);
        if (next.ExpiresAt <= Now) return new RefreshResult(RefreshStatus.Expired, UserId: existing.UserId);

        await using var tx = await db.Database.BeginTransactionAsync();

        // Claim the token with a conditional update rather than trusting the
        // read above: two requests racing with the same token would both
        // have seen it live. Only one claim can succeed; the loser is a reuse.
        var now = Now;
        var claimed = await db.RefreshTokens
            .Where(r => r.Id == existing.Id && r.RevokedAt == null)
            .ExecuteUpdateAsync(r => r
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevokedReason, RefreshTokenRevocation.Rotated));
        if (claimed == 0)
        {
            await tx.RollbackAsync();
            await RevokeFamilyAsync(existing.FamilyId, RefreshTokenRevocation.ReuseDetected);
            return new RefreshResult(RefreshStatus.ReuseDetected, UserId: existing.UserId, FamilyId: existing.FamilyId);
        }

        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync();
        await db.RefreshTokens
            .Where(r => r.Id == existing.Id)
            .ExecuteUpdateAsync(r => r.SetProperty(x => x.ReplacedByTokenId, next.Id));
        await tx.CommitAsync();

        return new RefreshResult(RefreshStatus.Ok, new IssuedTokens(await AccessTokenAsync(user), raw, next.ExpiresAt, next.FamilyId),
            user.Id, existing.FamilyId);
    }

    // Logout: ends this session (the whole family), leaves other devices alone.
    // Returns the session's owner and id, or null if the token wasn't live.
    public async Task<(string UserId, Guid FamilyId)?> RevokeAsync(string rawRefreshToken)
    {
        var existing = await FindAsync(rawRefreshToken);
        if (existing is null || existing.IsRevoked) return null;

        await RevokeFamilyAsync(existing.FamilyId, RefreshTokenRevocation.Logout);
        return (existing.UserId, existing.FamilyId);
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private Task<RefreshToken?> FindAsync(string rawToken)
    {
        var hash = Hash(rawToken);
        return db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash);
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason)
    {
        var now = Now;
        await db.RefreshTokens
            .Where(r => r.FamilyId == familyId && r.RevokedAt == null)
            .ExecuteUpdateAsync(r => r
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevokedReason, reason));
    }

    private (string Raw, RefreshToken Token) NewRefreshToken(string userId, Guid familyId, DateTime familyStartedAt)
    {
        var now = Now;
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expires = now.AddDays(Options.RefreshTokenDays);
        var familyEnds = familyStartedAt.AddDays(Options.RefreshFamilyDays);

        return (raw, new RefreshToken
        {
            TokenHash = Hash(raw),
            UserId = userId,
            FamilyId = familyId,
            FamilyStartedAt = familyStartedAt,
            CreatedAt = now,
            ExpiresAt = expires < familyEnds ? expires : familyEnds
        });
    }

    private async Task<string> AccessTokenAsync(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange((await userManager.GetRolesAsync(user)).Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Options.Key));
        var now = Now;
        var token = new JwtSecurityToken(
            issuer: Options.Issuer,
            audience: Options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(Options.AccessTokenMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
