using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.SavingsRate;

// The share of take-home pay kept this month, over twelve months of the
// same: neon bars for months that saved, torii for months that overspent.
public partial class SavingsRateCard(InsightClient client)
    : InsightCardViewModel<SavingsRateResponse>(client)
{
    protected override string Key => "savings-rate";
    public override string Title => "Savings rate";
    public override InsightGroup Group => InsightGroup.Savings;
    public override string EmptyMessage => "No income in the last twelve months.";

    [ObservableProperty] private string rateText = Format.None;
    [ObservableProperty] private bool isNegative;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private ISeries[] series = [];
    [ObservableProperty] private Axis[] xAxes = [];
    [ObservableProperty] private Axis[] yAxes = [];

    protected override bool Apply(SavingsRateResponse response)
    {
        RateText = Format.Percent(response.SavingsRate);
        IsNegative = response.SavingsRate < 0;
        Summary = response.SavingsRate == null
            ? "No income this month"
            : $"Kept {Format.Money(response.Income - response.Spending)} of {Format.Money(response.Income)} take-home";

        double? Percent(decimal? rate, bool saved) =>
            rate is { } r && r >= 0 == saved ? (double)(r * 100) : null;

        Series =
        [
            Bars("Saved", response.Months.Select(m => Percent(m.SavingsRate, saved: true)), "Neon"),
            Bars("Overspent", response.Months.Select(m => Percent(m.SavingsRate, saved: false)), "Torii")
        ];
        XAxes =
        [
            new Axis
            {
                Labels = response.Months.Select(m => Format.ShortMonth(m.Month)).ToArray(),
                LabelsPaint = ChartPalette.AxisLabels,
                TextSize = 11,
                ShowSeparatorLines = false
            }
        ];
        YAxes =
        [
            new Axis
            {
                Labeler = v => $"{v:0}%",
                LabelsPaint = ChartPalette.AxisLabels,
                SeparatorsPaint = ChartPalette.Gridlines,
                ZeroPaint = ChartPalette.AxisLabels,
                TextSize = 11
            }
        ];

        return response.Months.Any(m => m.SavingsRate != null);
    }

    private static ColumnSeries<double?> Bars(string name, IEnumerable<double?> values, string token) => new()
    {
        Name = name,
        Values = values.ToArray(),
        Fill = ChartPalette.Fill(token),
        MaxBarWidth = 14,
        // The two series never share a month, so overlap them in one slot.
        IgnoresBarPosition = true,
        Rx = 3,
        Ry = 3,
        YToolTipLabelFormatter = p => $"{p.Coordinate.PrimaryValue:0}%"
    };
}
