using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.CreditDebitSplit;

// This month's spending on credit (torii) vs debit (neon), with the
// average purchase on each.
public partial class CreditDebitSplitCard(InsightClient client)
    : InsightCardViewModel<CreditDebitSplitResponse>(client)
{
    protected override string Key => "credit-debit-split";
    public override string Title => "Credit vs debit";
    public override InsightGroup Group => InsightGroup.Spending;
    public override string EmptyMessage => "No card or bank spending this month.";

    [ObservableProperty] private string creditShareText = Format.None;
    [ObservableProperty] private string creditAmount = Format.None;
    [ObservableProperty] private string debitAmount = Format.None;
    [ObservableProperty] private string creditDetail = string.Empty;
    [ObservableProperty] private string debitDetail = string.Empty;
    [ObservableProperty] private ISeries[] series = [];

    protected override bool Apply(CreditDebitSplitResponse response)
    {
        CreditShareText = Format.Percent(response.Credit.Share);
        CreditAmount = Format.Money(response.Credit.Amount);
        DebitAmount = Format.Money(response.Debit.Amount);
        CreditDetail = Detail(response.Credit);
        DebitDetail = Detail(response.Debit);

        Series =
        [
            Slice("Credit", response.Credit.Amount, "Torii"),
            Slice("Debit", response.Debit.Amount, "Neon")
        ];

        return response.Credit.Amount + response.Debit.Amount > 0;
    }

    private static string Detail(MethodSpend spend) => spend.Purchases switch
    {
        0 => "No purchases",
        1 => "1 purchase",
        var n => $"{n} purchases · avg {(spend.AveragePurchase is { } a ? Format.Money(a) : Format.None)}"
    };

    private static ISeries Slice(string name, decimal amount, string token) => new PieSeries<double>
    {
        Name = name,
        Values = [(double)amount],
        Fill = ChartPalette.Fill(token),
        Stroke = new SolidColorPaint(ChartPalette.Sk("Surface")) { StrokeThickness = 3 },
        InnerRadius = 64,
        HoverPushout = 4,
        ToolTipLabelFormatter = _ => Format.Money(amount)
    };
}
