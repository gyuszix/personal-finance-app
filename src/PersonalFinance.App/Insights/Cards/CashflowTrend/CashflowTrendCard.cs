using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.CashflowTrend;

// Net cash flow for the selected month, over twelve months of income
// (neon) against spending (torii).
public partial class CashflowTrendCard(InsightClient client)
    : InsightCardViewModel<CashflowTrendResponse>(client)
{
    protected override string Key => "cashflow-trend";
    public override string Title => "Net cash flow";
    public override InsightGroup Group => InsightGroup.CashFlow;
    public override string EmptyMessage => "No income or spending in the last twelve months.";

    [ObservableProperty]
    private string netText = Format.None;

    [ObservableProperty]
    private string incomeText = Format.None;

    [ObservableProperty]
    private string expensesText = Format.None;

    [ObservableProperty]
    private bool isNetNegative;

    [ObservableProperty]
    private ISeries[] series = [];

    [ObservableProperty]
    private Axis[] xAxes = [];

    [ObservableProperty]
    private Axis[] yAxes = [];

    protected override bool Apply(CashflowTrendResponse response)
    {
        var current = response.Months.LastOrDefault();
        if (current != null)
        {
            NetText = (current.Net > 0 ? "+" : "") + Format.Money(current.Net);
            IsNetNegative = current.Net < 0;
            IncomeText = Format.Money(current.Income);
            ExpensesText = Format.Money(current.Expenses);
        }

        Series =
        [
            new ColumnSeries<double>
            {
                Name = "Income",
                Values = response.Months.Select(m => (double)m.Income).ToArray(),
                Fill = ChartPalette.Fill("Neon"),
                MaxBarWidth = 10,
                Padding = 1,
                Rx = 3,
                Ry = 3
            },
            new ColumnSeries<double>
            {
                Name = "Spending",
                Values = response.Months.Select(m => (double)m.Expenses).ToArray(),
                Fill = ChartPalette.Fill("Torii"),
                MaxBarWidth = 10,
                Padding = 1,
                Rx = 3,
                Ry = 3
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
                Labeler = Format.CompactMoney,
                LabelsPaint = ChartPalette.AxisLabels,
                SeparatorsPaint = ChartPalette.Gridlines,
                TextSize = 11,
                MinLimit = 0
            }
        ];

        return response.Months.Any(m => m.Income != 0 || m.Expenses != 0);
    }
}
