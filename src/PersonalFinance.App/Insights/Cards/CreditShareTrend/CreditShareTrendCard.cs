using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.CreditShareTrend;

// Credit's share of spending over twelve months - is the card being
// leaned on more? Months with no spending leave a gap, not a 0%.
public partial class CreditShareTrendCard(InsightClient client)
    : InsightCardViewModel<CreditShareTrendResponse>(client)
{
    protected override string Key => "credit-share-trend";
    public override string Title => "Credit share over time";
    public override InsightGroup Group => InsightGroup.Spending;
    public override string EmptyMessage => "No card or bank spending in the last twelve months.";

    [ObservableProperty] private string currentText = Format.None;
    [ObservableProperty] private string changeText = string.Empty;
    [ObservableProperty] private ISeries[] series = [];
    [ObservableProperty] private Axis[] xAxes = [];
    [ObservableProperty] private Axis[] yAxes = [];

    protected override bool Apply(CreditShareTrendResponse response)
    {
        var shares = response.Months.Select(m => m.CreditShare).ToList();
        CurrentText = Format.Percent(shares.LastOrDefault());

        // Against the average of the earlier months that had spending.
        var earlier = shares.SkipLast(1).Where(s => s.HasValue).Select(s => s!.Value).ToList();
        ChangeText = shares.LastOrDefault() is { } now && earlier.Count > 0
            ? $"{(now - earlier.Average() >= 0 ? "+" : "−")}{Math.Abs((now - earlier.Average()) * 100):0} pts vs your 11-month average"
            : string.Empty;

        Series =
        [
            new LineSeries<ObservablePoint>
            {
                Name = "Credit share",
                Values = shares.Select((s, i) => new ObservablePoint(i, s is { } v ? (double)(v * 100) : null)).ToArray(),
                Stroke = new SolidColorPaint(ChartPalette.Sk("Torii")) { StrokeThickness = 3 },
                Fill = ChartPalette.Fill("ToriiSoft"),
                GeometrySize = 7,
                GeometryStroke = new SolidColorPaint(ChartPalette.Sk("Torii")) { StrokeThickness = 2 },
                GeometryFill = ChartPalette.Fill("Surface"),
                LineSmoothness = 0.35,
                YToolTipLabelFormatter = p => $"{p.Coordinate.PrimaryValue:0}% on credit"
            }
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
                MinLimit = 0,
                MaxLimit = 100,
                MinStep = 25,
                Labeler = v => $"{v:0}%",
                LabelsPaint = ChartPalette.AxisLabels,
                SeparatorsPaint = ChartPalette.Gridlines,
                TextSize = 11
            }
        ];

        return shares.Any(s => s.HasValue);
    }
}
