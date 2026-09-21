using PersonalFinance.App.Views;

namespace PersonalFinance.App.Services;

// Presents Plaid Link via PlaidLinkPage's WebView. Works on every MAUI
// platform without a native SDK - #32/#33 can swap this per-platform later
// if a native experience turns out to matter, but this is a real, working
// bank-linking flow today.
public class PlaidLinkWebService : IPlaidLinkService
{
    public async Task<string?> OpenLinkAsync(string linkToken)
    {
        var page = new PlaidLinkPage(linkToken);
        await Shell.Current.Navigation.PushModalAsync(page);
        return await page.WaitForResultAsync();
    }
}
