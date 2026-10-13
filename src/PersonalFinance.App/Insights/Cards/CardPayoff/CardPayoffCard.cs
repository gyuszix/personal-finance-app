using CommunityToolkit.Mvvm.ComponentModel;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.CardPayoff;

// Card payments ÷ credit spending. Neon at 100% or more (paid in full),
// orange below it (a balance is building).
public partial class CardPayoffCard(InsightClient client)
    : InsightCardViewModel<CardPayoffResponse>(client)
{
    protected override string Key => "card-payoff";
    public override string Title => "Card payoff";
    public override InsightGroup Group => InsightGroup.Spending;
    public override string EmptyMessage => "No credit card spending this month.";

    [ObservableProperty] private string ratioText = Format.None;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private string verdict = string.Empty;
    [ObservableProperty] private bool isPaidInFull;
    [ObservableProperty] private double progress;

    protected override bool Apply(CardPayoffResponse response)
    {
        RatioText = Format.Percent(response.PayoffRatio);
        Summary = $"Paid {Format.Money(response.CardPayments)} of {Format.Money(response.CreditSpending)} charged";
        IsPaidInFull = response.PayoffRatio >= 1;
        Verdict = IsPaidInFull ? "Paid in full" : "A balance is carrying over";
        Progress = (double)Math.Min(response.PayoffRatio ?? 0, 1);
        return response.PayoffRatio != null;
    }
}
