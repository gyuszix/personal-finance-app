namespace PersonalFinance.Shared.DTOs;

// TOTP multi-factor authentication (#117).

public class MfaStatusResponse
{
    public bool Enabled { get; set; }
    public int RecoveryCodesLeft { get; set; }
}

public class MfaSetupResponse
{
    // Base32 secret, grouped in fours for typing into an authenticator app.
    public string SharedKey { get; set; } = string.Empty;

    // otpauth:// URI - what a QR code would encode.
    public string AuthenticatorUri { get; set; } = string.Empty;
}

public class MfaRecoveryCodesResponse
{
    public List<string> RecoveryCodes { get; set; } = [];
}

// A 6-digit authenticator code, or (where accepted) a recovery code.
public class MfaCodeRequest
{
    public string Code { get; set; } = string.Empty;
}

public static class MfaProblemCodes
{
    // Login problem "code" extension when the password was right but the
    // account needs a second factor: ask for it and send the login again.
    public const string RequiresTwoFactor = "RequiresTwoFactor";
    public const string InvalidTwoFactorCode = "InvalidTwoFactorCode";
}
