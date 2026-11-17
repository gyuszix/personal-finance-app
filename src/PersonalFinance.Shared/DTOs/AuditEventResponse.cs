using System.Text.Json;

namespace PersonalFinance.Shared.DTOs;

public class AuditEventResponse
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public JsonElement? Details { get; set; }
}
