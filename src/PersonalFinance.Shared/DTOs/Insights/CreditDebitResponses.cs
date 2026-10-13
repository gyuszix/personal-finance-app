namespace PersonalFinance.Shared.DTOs.Insights;

// Credit = spent from credit card accounts; debit = spent from bank
// (depository) accounts (#87).

public class CreditDebitSplitResponse
{
    public string Month { get; set; } = string.Empty;
    public MethodSpend Credit { get; set; } = new();
    public MethodSpend Debit { get; set; } = new();
}

public class MethodSpend
{
    public decimal Amount { get; set; }
    // Of credit + debit, 0-1; null when nothing was spent.
    public decimal? Share { get; set; }
    public int Purchases { get; set; }
    // Null with no purchases.
    public decimal? AveragePurchase { get; set; }
}

public class CreditShareTrendResponse
{
    // 12 months ending with the requested one, oldest first.
    public List<CreditShareMonth> Months { get; set; } = [];
}

public class CreditShareMonth
{
    public string Month { get; set; } = string.Empty;
    public decimal Credit { get; set; }
    public decimal Debit { get; set; }
    public decimal? CreditShare { get; set; }
}

public class CreditDebitByCategoryResponse
{
    public string Month { get; set; } = string.Empty;
    public List<CategoryMethodSpend> Categories { get; set; } = [];   // largest first
}

public class CategoryMethodSpend
{
    public string Category { get; set; } = string.Empty;
    public decimal Credit { get; set; }
    public decimal Debit { get; set; }
    public decimal? CreditShare { get; set; }
}

public class CardPayoffResponse
{
    public string Month { get; set; } = string.Empty;
    public decimal CreditSpending { get; set; }
    public decimal CardPayments { get; set; }
    // Payments ÷ credit spending; null with no credit spending.
    public decimal? PayoffRatio { get; set; }
}
