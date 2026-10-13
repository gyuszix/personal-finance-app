using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.App.Insights.Cards.BudgetVariance;

// Spent vs limit per category this month: a neon bar inside the limit,
// torii once it's over. Limits are suggested from recent spending until
// the user sets them on the Budgets page.
public partial class BudgetVarianceCard(InsightClient client)
    : InsightCardViewModel<BudgetVarianceResponse>(client)
{
    protected override string Key => "budget-variance";
    public override string Title => "Budgets";
    public override InsightGroup Group => InsightGroup.Spending;
    public override string EmptyMessage => "No spending to budget yet.";

    [ObservableProperty] private string headline = Format.None;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private bool hasSuggestions;
    [ObservableProperty] private IReadOnlyList<BudgetRow> rows = [];

    protected override bool Apply(BudgetVarianceResponse response)
    {
        Headline = response.OverBudget switch
        {
            0 => "On budget",
            1 => "1 category over",
            var n => $"{n} categories over"
        };
        Summary = $"{Format.Money(response.TotalSpent)} of {Format.Money(response.TotalLimit)} budgeted this month";
        HasSuggestions = response.Categories.Any(c => c.IsSuggested);
        Rows = response.Categories.Select(c =>
        {
            var used = c.Limit == 0 ? (c.Spent > 0 ? 1d : 0d) : Math.Min((double)(c.Spent / c.Limit), 1);
            return new BudgetRow(
                Format.Category(c.Category) + (c.IsSuggested ? " ·" : ""),
                $"{Format.Money(c.Spent)} / {Format.Money(c.Limit)}",
                c.Remaining < 0 ? $"{Format.Money(-c.Remaining)} over" : $"{Format.Money(c.Remaining)} left",
                c.Remaining < 0,
                new GridLength(used, GridUnitType.Star),
                new GridLength(1 - used, GridUnitType.Star));
        }).ToList();

        return response.Categories.Count > 0;
    }

    [RelayCommand]
    private static Task EditBudgetsAsync() => Shell.Current.GoToAsync("budgets");
}

public record BudgetRow(string Name, string Amounts, string Remaining, bool IsOver, GridLength Used, GridLength Rest);
