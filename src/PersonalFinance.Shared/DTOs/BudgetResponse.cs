namespace PersonalFinance.Shared.DTOs;

public class BudgetResponse
{
    public string Category { get; set; } = string.Empty;
    // The limit in effect: the saved one, or SuggestedLimit if none is saved.
    public decimal MonthlyLimit { get; set; }
    // Average monthly spend over the last three complete months, rounded up
    // to the next 10.
    public decimal SuggestedLimit { get; set; }
    public bool IsSuggested { get; set; }
}

public class SetBudgetBody
{
    public decimal MonthlyLimit { get; set; }
}
