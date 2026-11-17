namespace PersonalFinance.Api.DTOs;

// RefreshToken is omitted by browser clients, whose token travels in the
// HttpOnly cookie instead (#115).
public record RefreshRequest(string? RefreshToken);
