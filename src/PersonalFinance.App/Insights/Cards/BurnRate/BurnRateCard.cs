using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.BurnRate;

// Average monthly spending, and whether this month is running hotter
// (orange) or cooler (neon) than that.
public partial class BurnRateCard(InsightClient client)
    : InsightCardViewModel<BurnRateResponse>(client)
{
    protected override string Key => "burn-rate";
    public override string Title => "Burn rate";
    public override InsightGroup Group => InsightGroup.CashFlow;
    public override string EmptyMessage => "Not enough spending history yet - this needs a full month.";

    [ObservableProperty] private string burnText = Format.None;
    [ObservableProperty] private string windowText = string.Empty;
    [ObservableProperty] private string paceText = Format.None;
    [ObservableProperty] private string paceDetail = string.Empty;
    [ObservableProperty] private bool isOverPace;
    [ObservableProperty] private double progress;

    protected override bool Apply(BurnRateResponse response)
    {
        BurnText = response.MonthlyBurnRate is { } burn ? Format.Money(burn) : Format.None;
        WindowText = $"a month, averaged over {Format.ShortMonth(response.WindowStart)}–{Format.ShortMonth(response.WindowEnd)}";

        var isComplete = response.DaysElapsed == response.DaysInMonth;
        PaceText = Format.Percent(response.PaceVsBurnRate);
        PaceDetail = isComplete
            ? $"Spent {Format.Money(response.SpentSoFar)} this month"
            : $"{Format.Money(response.SpentSoFar)} so far, day {response.DaysElapsed.ToString(CultureInfo.CurrentCulture)} of {response.DaysInMonth} - on pace for {Format.Money(response.ProjectedSpending)}";
        IsOverPace = response.PaceVsBurnRate > 1;
        Progress = response.MonthlyBurnRate is { } b and > 0 ? (double)Math.Min(response.SpentSoFar / b, 1) : 0;

        return response.MonthlyBurnRate != null;
    }
}
