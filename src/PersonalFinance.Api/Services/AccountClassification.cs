namespace PersonalFinance.Api.Services;

public static class AccountClassification
{
    public const string Asset = "Asset";
    public const string Liability = "Liability";
    public const string Other = "Other";

    // Plaid's own account type classification - depository/investment
    // accounts are assets, credit/loan accounts are liabilities (balance
    // represents amount owed, not negative equity). Anything unrecognized
    // is "Other" and deliberately excluded from both totals rather than
    // guessed at.
    public static string Of(string accountType) => accountType switch
    {
        "Depository" or "Investment" => Asset,
        "Credit" or "Loan" => Liability,
        _ => Other
    };

    // Depository subtypes that are cash you can reach today. CDs, HSAs and
    // EBT are depository too but locked up or restricted. A depository
    // account with no subtype yet (linked before #83, not synced since)
    // counts as liquid - it's almost always checking or savings.
    private static readonly HashSet<string> LiquidSubtypes =
        ["Checking", "Savings", "MoneyMarket", "CashManagement", "Paypal", "Prepaid"];

    public static bool IsLiquid(string accountType, string? subtype) =>
        accountType == "Depository" && (subtype is null || LiquidSubtypes.Contains(subtype));
}
