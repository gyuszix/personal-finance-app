namespace PersonalFinance.App.Insights;

// The Dashboard's tabs, in display order. A tab only shows when at least one
// registered card belongs to it.
public enum InsightGroup
{
    Spending,
    CashFlow,
    Savings,
    Debt,
    NetWorth
}

public static class InsightGroupNames
{
    public static string Display(this InsightGroup group) => group switch
    {
        InsightGroup.CashFlow => "Cash Flow",
        InsightGroup.NetWorth => "Net Worth",
        _ => group.ToString()
    };
}
