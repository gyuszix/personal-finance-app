using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace PersonalFinance.App.Insights;

// Chart colours come from the same Colors.xaml tokens as the rest of the UI
// (#86), so the charts never carry their own hex values.
public static class ChartPalette
{
    // Series colours in order: torii and neon first, then orange and blue.
    public static readonly string[] SeriesTokens = ["Torii", "Neon", "Orange", "Blue", "NeonGlow", "TextMuted"];

    public static Color Color(string token) =>
        Application.Current?.Resources.TryGetValue(token, out var value) == true && value is Color color
            ? color
            : Colors.Gray;

    public static Color Series(int index) => Color(SeriesTokens[index % SeriesTokens.Length]);

    public static SKColor Sk(string token) => Color(token).ToSKColor();

    public static SolidColorPaint Fill(Color color) => new(color.ToSKColor());

    public static SolidColorPaint Fill(string token) => new(Sk(token));

    // Axis labels and gridlines: muted text on hairline grid.
    public static SolidColorPaint AxisLabels => new(Sk("TextMuted")) { SKTypeface = SKTypeface.Default };
    public static SolidColorPaint Gridlines => new(Sk("Hairline")) { StrokeThickness = 1 };
    public static SolidColorPaint TooltipBackground => new(Sk("SurfaceRaised"));
    public static SolidColorPaint TooltipText => new(Sk("TextPrimary"));
}
