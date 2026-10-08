using Going.Plaid;
using Going.Plaid.Entity;
using Going.Plaid.Institutions;

namespace PersonalFinance.Api.Services;

// Turns Plaid's institution ID (e.g. "ins_109508") into the bank's display
// name (e.g. "First Platypus Bank"). Account.BankName used to store the raw
// ID, which is what every client ended up showing.
public class PlaidInstitutionService(PlaidClient plaid, ILogger<PlaidInstitutionService> logger)
{
    // Plaid institution IDs all look like this; real names never do. Lets
    // sync spot rows linked before names were resolved and fix them.
    public static bool IsRawInstitutionId(string? bankName) =>
        bankName is not null && bankName.StartsWith("ins_", StringComparison.Ordinal);

    // Returns the institution's name, or null if Plaid couldn't resolve it -
    // callers fall back to the raw ID so a later sync can retry.
    public async Task<string?> GetNameAsync(string? institutionId)
    {
        if (string.IsNullOrEmpty(institutionId)) return null;

        var response = await plaid.InstitutionsGetByIdAsync(new InstitutionsGetByIdRequest
        {
            InstitutionId = institutionId,
            CountryCodes = [CountryCode.Us]
        });

        if (response.Error is not null || string.IsNullOrEmpty(response.Institution?.Name))
        {
            logger.LogWarning(
                "Couldn't resolve Plaid institution {InstitutionId}: {Error}",
                institutionId, response.Error?.ErrorMessage ?? "no name returned");
            return null;
        }

        return response.Institution.Name;
    }
}
