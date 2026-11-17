namespace PersonalFinance.Api.DTOs;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // Only for accounts with MFA on: one of these, sent with the password
    // again after a 401 RequiresTwoFactor (#117).
    public string? TwoFactorCode { get; set; }
    public string? TwoFactorRecoveryCode { get; set; }
}
