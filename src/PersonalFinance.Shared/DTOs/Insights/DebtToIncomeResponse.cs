namespace PersonalFinance.Shared.DTOs.Insights;

// Debt-to-income from take-home pay (#89). Monthly figures are averages
// over WindowStart..WindowEnd.
public class DebtToIncomeResponse
{
    public string Month { get; set; } = string.Empty;
    public string WindowStart { get; set; } = string.Empty;
    public string WindowEnd { get; set; } = string.Empty;
    public decimal MonthlyIncome { get; set; }
    public decimal MonthlyDebtPayments { get; set; }
    // Payments ÷ income; null with no income.
    public decimal? DebtToIncome { get; set; }
    // By Plaid detailed category (LOAN_PAYMENTS_CAR_PAYMENT, ...), largest first.
    public List<DebtPaymentType> Payments { get; set; } = [];
}

public class DebtPaymentType
{
    public string Type { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }
}
