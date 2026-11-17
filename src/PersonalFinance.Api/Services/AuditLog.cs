using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Services;

// Records security events: a row in AuditEvents (durable, queryable per
// user) and a structured log entry (for the log pipeline) (#116).
public class AuditLog(AppDbContext db, IHttpContextAccessor http, TimeProvider time, ILogger<AuditLog> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(string eventType, string? userId, object? details = null, CancellationToken cancellationToken = default)
    {
        var request = http.HttpContext?.Request;
        var auditEvent = new AuditEvent
        {
            OccurredAt = time.GetUtcNow().UtcDateTime,
            UserId = userId,
            EventType = eventType,
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Truncate(request?.Headers.UserAgent.ToString(), 256),
            Details = details is null ? null : JsonSerializer.Serialize(details, Json)
        };

        // Written straight to the table, not through the change tracker: an
        // audit write must not save (or be lost with) whatever else the
        // request happens to have pending. Inside a caller's transaction it
        // commits or rolls back with it, which is what we want.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "AuditEvents" ("OccurredAt", "UserId", "EventType", "IpAddress", "UserAgent", "Details")
            VALUES ({auditEvent.OccurredAt}, {auditEvent.UserId}, {auditEvent.EventType}, {auditEvent.IpAddress},
                    {auditEvent.UserAgent}, CAST({auditEvent.Details} AS jsonb))
            """, cancellationToken);

        logger.LogInformation(
            "Audit {AuditEventType} for user {UserId} from {IpAddress}: {AuditDetails}",
            eventType, userId, auditEvent.IpAddress, auditEvent.Details);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
