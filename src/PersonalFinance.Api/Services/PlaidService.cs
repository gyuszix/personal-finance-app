namespace PersonalFinance.Api.Services;

// Thin wrapper over the Plaid API: create link tokens, exchange public tokens,
// call /transactions/sync with the stored cursor. Encrypts/decrypts access tokens
// via ASP.NET Data Protection — raw tokens never leave this service.
public class PlaidService
{
}
