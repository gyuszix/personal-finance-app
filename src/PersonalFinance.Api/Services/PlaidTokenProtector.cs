using Microsoft.AspNetCore.DataProtection;

namespace PersonalFinance.Api.Services;

// Wraps IDataProtector so Plaid access tokens are never stored in plaintext.
// The purpose string ("PlaidAccessToken") scopes the protector - tokens
// protected here can only be unprotected by a protector created with the
// same purpose, so this can't accidentally decrypt/be decrypted by other
// data protected elsewhere in the app.
public class PlaidTokenProtector
{
    private readonly IDataProtector _protector;

    public PlaidTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("PlaidAccessToken");
    }

    public string Protect(string accessToken) => _protector.Protect(accessToken);

    public string Unprotect(string protectedAccessToken) => _protector.Unprotect(protectedAccessToken);
}
