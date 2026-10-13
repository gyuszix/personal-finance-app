namespace PersonalFinance.Api.Entities;

// A monthly spending limit the user saved for one Plaid primary category.
// Categories without one use a suggested limit instead (#88).
public class Budget
{
    public int BudgetId { get; set; }
    public string UserId { get; set; }
    public string Category { get; set; }
    public decimal MonthlyLimit { get; set; }
}
