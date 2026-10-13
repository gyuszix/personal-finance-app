using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.EmergencyFund;

// How many months of usual spending your cash covers, against the common
// 3-6 month guideline: torii under one month, orange under three, neon from
// three up.
public partial class EmergencyFundCard(InsightClient client)
    : InsightCardViewModel<EmergencyFundResponse>(client)
{
    private const double GuidelineMonths = 6;

    protected override string Key => "emergency-fund";
    public override string Title => "Emergency fund";
    public override InsightGroup Group => InsightGroup.Savings;
    public override string EmptyMessage => "No checking or savings accounts linked.";

    [ObservableProperty] private string monthsText = Format.None;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private Color figureColor = ChartPalette.Color("TextPrimary");
    [ObservableProperty] private double progress;

    protected override bool Apply(EmergencyFundResponse response)
    {
        var months = response.MonthsCovered;
        MonthsText = months is { } m ? $"{m.ToString("0.0", CultureInfo.CurrentCulture)} months" : Format.None;
        Summary = response.MonthlyBurnRate is { } burn
            ? $"{Format.Money(response.LiquidCash)} in cash ÷ {Format.Money(burn)} a month"
            : $"{Format.Money(response.LiquidCash)} in cash - not enough spending history to compare yet";
        FigureColor = ChartPalette.Color(months switch
        {
            null => "TextPrimary",
            < 1 => "Negative",
            < 3 => "Warning",
            _ => "Positive"
        });
        Progress = months is { } p ? Math.Min((double)p / GuidelineMonths, 1) : 0;

        return response.LiquidAccounts > 0;
    }
}
