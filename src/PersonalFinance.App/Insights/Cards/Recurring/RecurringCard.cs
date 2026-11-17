using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.Recurring;

// Subscriptions and bills found in your spending (#125): what they cost a
// month in total, and the next few charges coming up.
public partial class RecurringCard(InsightClient client)
    : InsightCardViewModel<RecurringResponse>(client)
{
    private const int Shown = 6;

    protected override string Key => "recurring";
    public override string Title => "Recurring";
    public override InsightGroup Group => InsightGroup.CashFlow;
    public override string EmptyMessage => "No recurring charges found yet - they show up after three regular payments.";

    [ObservableProperty] private string totalText = Format.None;
    [ObservableProperty] private string countText = string.Empty;
    [ObservableProperty] private IReadOnlyList<RecurringRow> rows = [];
    [ObservableProperty] private string moreText = string.Empty;
    [ObservableProperty] private bool hasMore;

    protected override bool Apply(RecurringResponse response)
    {
        TotalText = $"{Format.Money(response.TotalMonthlyCost)} a month";
        CountText = response.Series.Count == 1 ? "1 recurring charge" : $"{response.Series.Count} recurring charges";
        Rows = response.Series.Take(Shown).Select(s => new RecurringRow(
            s.Merchant,
            $"{s.Cadence} · {Format.Category(s.Category ?? "")}",
            s.TypicalAmount.ToString("C", CultureInfo.CurrentCulture),
            "next " + s.NextDate.ToString("MMM d", CultureInfo.CurrentCulture))).ToList();
        HasMore = response.Series.Count > Shown;
        MoreText = HasMore ? $"and {response.Series.Count - Shown} more" : string.Empty;
        return response.Series.Count > 0;
    }
}

public record RecurringRow(string Merchant, string Detail, string Amount, string Next);
