namespace PersonalFinance.Api.Entities;

// One security-relevant event. Append-only: the database rejects UPDATE and
// DELETE on this table (#116). Never holds secrets - no passwords, tokens or
// Plaid access tokens, in Details or anywhere else.
public class AuditEvent
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }

    // Null when there's no known user (e.g. a failed login for an email that
    // isn't registered, a rejected webhook).
    public string? UserId { get; set; }

    public string EventType { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    // Small JSON object (jsonb) with event-specific context.
    public string? Details { get; set; }
}

public static class AuditEventTypes
{
    public const string LoginSucceeded = "auth.login.succeeded";
    public const string LoginFailed = "auth.login.failed";
    public const string RefreshTokenReuseDetected = "auth.refresh.reuse_detected";
    public const string Logout = "auth.logout";
    public const string PlaidItemLinked = "plaid.item.linked";
    public const string PlaidItemUnlinked = "plaid.item.unlinked";
}
