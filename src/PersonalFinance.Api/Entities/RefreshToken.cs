namespace PersonalFinance.Api.Entities;

// One refresh token. Tokens issued by rotating each other share a FamilyId:
// one family is one login on one device (#115).
public class RefreshToken
{
    public int Id { get; set; }

    // SHA-256 of the token, never the token itself: a leaked database
    // doesn't leak usable sessions. Tokens are 512 random bits, so a fast
    // unsalted hash is enough (nothing to brute-force, unlike a password).
    public string TokenHash { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;
    public Guid FamilyId { get; set; }

    // When the family's first token was issued (the login). Rotation can't
    // extend a session past FamilyStartedAt + the family lifetime.
    public DateTime FamilyStartedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public int? ReplacedByTokenId { get; set; }

    public bool IsRevoked => RevokedAt is not null;
}

public static class RefreshTokenRevocation
{
    public const string Rotated = "rotated";
    public const string Logout = "logout";
    public const string ReuseDetected = "reuse-detected";
}
