namespace PersonalFinance.Shared;

// Plaid's personal_finance_category primaries - the categories a
// transaction can have, and a categorisation rule can assign (#124).
public static class PlaidCategories
{
    public static readonly IReadOnlyList<string> Primary =
    [
        "BANK_FEES", "ENTERTAINMENT", "FOOD_AND_DRINK", "GENERAL_MERCHANDISE",
        "GENERAL_SERVICES", "GOVERNMENT_AND_NON_PROFIT", "HOME_IMPROVEMENT",
        "INCOME", "LOAN_PAYMENTS", "MEDICAL", "PERSONAL_CARE", "RENT_AND_UTILITIES",
        "TRANSFER_IN", "TRANSFER_OUT", "TRANSPORTATION", "TRAVEL"
    ];
}
