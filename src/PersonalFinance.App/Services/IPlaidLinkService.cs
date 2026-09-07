namespace PersonalFinance.App.Services;

// Presents Plaid Link and returns the resulting public_token. One
// per-platform implementation (see #32 for iOS/Mac Catalyst, #33 for
// Android - both behind this same contract), so the rest of the app
// never needs to know how Link was actually presented.
public interface IPlaidLinkService
{
    // Returns the public_token on success, or null if the user cancelled/
    // an error occurred before completion.
    Task<string?> OpenLinkAsync(string linkToken);
}
