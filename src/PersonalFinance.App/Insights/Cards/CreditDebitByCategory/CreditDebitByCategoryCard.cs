using CommunityToolkit.Mvvm.ComponentModel;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.CreditDebitByCategory;

// Per category, how the month's spending split between credit and debit:
// one bar each, scaled to the largest category so sizes compare.
public partial class CreditDebitByCategoryCard(InsightClient client)
    : InsightCardViewModel<CreditDebitByCategoryResponse>(client)
{
    private const int Rows = 6;

    protected override string Key => "credit-debit-by-category";
    public override string Title => "Credit vs debit by category";
    public override InsightGroup Group => InsightGroup.Spending;
    public override string EmptyMessage => "No card or bank spending this month.";

    [ObservableProperty] private IReadOnlyList<MethodBar> bars = [];

    protected override bool Apply(CreditDebitByCategoryResponse response)
    {
        var categories = response.Categories.Take(Rows).ToList();
        var largest = categories.Select(c => c.Credit + c.Debit).DefaultIfEmpty(0).Max();

        Bars = categories.Select(c => new MethodBar(
                Format.Category(c.Category),
                Format.Money(c.Credit + c.Debit),
                c.CreditShare is { } s ? $"{Format.Percent(s)} credit" : Format.None,
                Star(c.Credit, largest),
                Star(c.Debit, largest),
                Star(largest - c.Credit - c.Debit, largest)))
            .ToList();

        return largest > 0;
    }

    private static GridLength Star(decimal value, decimal largest) =>
        new(largest == 0 ? 0 : (double)(value / largest), GridUnitType.Star);
}

public record MethodBar(string Name, string Total, string CreditShare, GridLength Credit, GridLength Debit, GridLength Rest);
