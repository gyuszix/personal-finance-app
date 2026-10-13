using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.NetWorthTrend;

// Net worth per day as a neon line over 30 days, 90 days or a year. The
// history only goes back to when balance snapshots started (#84).
public partial class NetWorthTrendCard(InsightClient client)
    : InsightCardViewModel<NetWorthTrendResponse>(client)
{
    private IReadOnlyList<NetWorthPoint> _points = [];

    protected override string Key => "net-worth-trend";
    public override string Title => "Net worth trend";
    public override InsightGroup Group => InsightGroup.NetWorth;
    public override string EmptyMessage => "History starts with your next sync - check back in a few days.";

    public IReadOnlyList<RangeOption> Ranges { get; } = [new("30D", 30), new("90D", 90), new("1Y", 365)];

    [ObservableProperty] private string changeText = Format.None;
    [ObservableProperty] private bool isChangeNegative;
    [ObservableProperty] private string rangeText = string.Empty;
    [ObservableProperty] private ISeries[] series = [];
    [ObservableProperty] private Axis[] xAxes = [];
    [ObservableProperty] private Axis[] yAxes = [];

    protected override bool Apply(NetWorthTrendResponse response)
    {
        _points = response.Points;
        if (Ranges.All(r => !r.IsSelected)) Ranges[0].IsSelected = true;
        Draw();
        return _points.Count > 0;
    }

    [RelayCommand]
    private void SelectRange(RangeOption range)
    {
        foreach (var r in Ranges) r.IsSelected = r == range;
        Draw();
    }

    private void Draw()
    {
        var days = Ranges.First(r => r.IsSelected).Days;
        var points = _points.TakeLast(days).ToList();
        if (points.Count == 0) return;

        var change = points[^1].NetWorth - points[0].NetWorth;
        ChangeText = (change > 0 ? "+" : "") + Format.Money(change);
        IsChangeNegative = change < 0;
        RangeText = points.Count < days
            ? $"since {Date(points[0]).ToString("MMM d", CultureInfo.CurrentCulture)}, when history starts"
            : days == 365 ? "over the last year" : $"over the last {days} days";

        Series =
        [
            new LineSeries<DateTimePoint>
            {
                Name = "Net worth",
                Values = points.Select(p => new DateTimePoint(Date(p), (double)p.NetWorth)).ToArray(),
                Stroke = new SolidColorPaint(ChartPalette.Sk("Neon")) { StrokeThickness = 3 },
                Fill = ChartPalette.Fill("NeonSoft"),
                GeometrySize = 0,
                LineSmoothness = 0.2,
                YToolTipLabelFormatter = p => Format.Money((decimal)p.Coordinate.PrimaryValue)
            }
        ];
        XAxes =
        [
            new DateTimeAxis(TimeSpan.FromDays(1), d => d.ToString("MMM d", CultureInfo.CurrentCulture))
            {
                LabelsPaint = ChartPalette.AxisLabels,
                TextSize = 11,
                ShowSeparatorLines = false
            }
        ];
        YAxes =
        [
            new Axis
            {
                Labeler = Format.CompactMoney,
                LabelsPaint = ChartPalette.AxisLabels,
                SeparatorsPaint = ChartPalette.Gridlines,
                TextSize = 11
            }
        ];
    }

    private static DateTime Date(NetWorthPoint point) =>
        DateTime.ParseExact(point.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public partial class RangeOption(string name, int days) : ObservableObject
{
    public string Name { get; } = name;
    public int Days { get; } = days;

    [ObservableProperty]
    private bool isSelected;
}
