namespace PersonalFinance.App.Services;

// Presents Plaid Link and returns the resulting public_token. Currently
// backed by PlaidLinkWebService (a WebView hosting Plaid's JS SDK), which
// works on every platform - #32/#33 track swapping in native SDKs later if
// that turns out to matter, behind this same contract.
public interface IPlaidLinkService
{
    // Returns the public_token on success, or null if the user cancelled/
    // an error occurred before completion.
    Task<string?> OpenLinkAsync(string linkToken);
}
