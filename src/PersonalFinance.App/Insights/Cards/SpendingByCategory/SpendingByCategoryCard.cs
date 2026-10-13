using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.SpendingByCategory;

// The month's spending as a donut: the five largest categories, the rest
// folded into "Other", and the total in the middle.
public partial class SpendingByCategoryCard(InsightClient client)
    : InsightCardViewModel<SpendingByCategoryResponse>(client)
{
    private const int Slices = 5;

    protected override string Key => "spending-by-category";
    public override string Title => "Spending by category";
    public override InsightGroup Group => InsightGroup.Spending;
    public override string EmptyMessage => "No spending recorded this month.";

    [ObservableProperty]
    private string totalText = Format.None;

    [ObservableProperty]
    private ISeries[] series = [];

    [ObservableProperty]
    private IReadOnlyList<DonutSlice> legend = [];

    protected override bool Apply(SpendingByCategoryResponse response)
    {
        var top = response.Categories.Take(Slices)
            .Select(c => (Name: Format.Category(c.Category), c.Amount, c.Share))
            .ToList();
        var rest = response.Categories.Skip(Slices).ToList();
        if (rest.Count > 0)
        {
            var amount = rest.Sum(c => c.Amount);
            top.Add(("Other", amount, response.Total == 0 ? null : amount / response.Total));
        }

        // "Other" is always muted so it never reads as a sixth category.
        var slices = top.Select((s, i) => new DonutSlice(
                s.Name,
                Format.Money(s.Amount),
                Format.Percent(s.Share),
                s.Name == "Other" && rest.Count > 0 ? ChartPalette.Color("TextMuted") : ChartPalette.Series(i),
                s.Amount))
            .ToList();

        TotalText = Format.Money(response.Total);
        Legend = slices;
        Series = slices.Select(s => (ISeries)new PieSeries<double>
        {
            Name = s.Name,
            Values = [(double)s.Value],
            Fill = ChartPalette.Fill(s.Color),
            // A hairline of the card's own colour separates the slices.
            Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(ChartPalette.Sk("Surface")) { StrokeThickness = 3 },
            InnerRadius = 70,
            HoverPushout = 4,
            ToolTipLabelFormatter = _ => $"{s.Amount}  ·  {s.Share}"
        }).ToArray();

        return response.Total > 0;
    }
}

public record DonutSlice(string Name, string Amount, string Share, Color Color, decimal Value);
