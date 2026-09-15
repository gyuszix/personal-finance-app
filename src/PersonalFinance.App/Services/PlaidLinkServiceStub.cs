namespace PersonalFinance.App.Services;

// Temporary stand-in for IPlaidLinkService until #32/#33 land a real native
// implementation. Presents a picker so the "connect a bank" flow can be
// exercised end-to-end (success/cancel/error) without a real Plaid Link SDK.
public class PlaidLinkServiceStub : IPlaidLinkService
{
    public async Task<string?> OpenLinkAsync(string linkToken)
    {
        var choice = await Shell.Current.DisplayActionSheetAsync(
            "Simulate Plaid Link (stub - real Link UI lands in #32/#33)",
            "Simulate Cancel",
            null,
            "Simulate Success",
            "Simulate Error");

        if (choice == "Simulate Error")
            throw new InvalidOperationException("Simulated Plaid Link error");

        return choice == "Simulate Success" ? "public-sandbox-stub-token" : null;
    }
}
