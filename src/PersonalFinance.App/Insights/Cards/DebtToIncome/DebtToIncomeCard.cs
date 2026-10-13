using CommunityToolkit.Mvvm.ComponentModel;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.DebtToIncome;

// The share of take-home pay that goes to loan payments. Lenders measure
// against gross pay, so their thresholds aren't shown - this reads higher.
public partial class DebtToIncomeCard(InsightClient client)
    : InsightCardViewModel<DebtToIncomeResponse>(client)
{
    protected override string Key => "debt-to-income";
    public override string Title => "Debt-to-income (take-home)";
    public override InsightGroup Group => InsightGroup.Debt;
    public override string EmptyMessage => "No income or loan payments in the last three months.";

    [ObservableProperty] private string ratioText = Format.None;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private IReadOnlyList<PaymentRow> payments = [];
    [ObservableProperty] private bool hasPayments;

    protected override bool Apply(DebtToIncomeResponse response)
    {
        RatioText = Format.Percent(response.DebtToIncome);
        Summary = $"{Format.Money(response.MonthlyDebtPayments)} of {Format.Money(response.MonthlyIncome)} a month, " +
                  $"{Format.ShortMonth(response.WindowStart)}–{Format.ShortMonth(response.WindowEnd)} average";
        Payments = response.Payments
            .Select(p => new PaymentRow(Format.Category(p.Type.Replace("LOAN_PAYMENTS_", "")), Format.Money(p.MonthlyAmount)))
            .ToList();
        HasPayments = Payments.Count > 0;

        return response.MonthlyIncome > 0 || response.MonthlyDebtPayments > 0;
    }
}

public record PaymentRow(string Name, string Amount);
