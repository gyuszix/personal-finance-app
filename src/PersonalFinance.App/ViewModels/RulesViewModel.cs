using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.App.Insights;
using PersonalFinance.App.Services;
using PersonalFinance.Shared;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.App.ViewModels;

// Categorisation rules (#124): "description contains X → category Y",
// optionally only for a range of amounts. The server re-categorises every
// matching transaction - past and future - as soon as a rule is saved.
public partial class RulesViewModel(ApiService api) : ObservableObject
{
    public ObservableCollection<RuleItem> Rules { get; } = [];
    public IReadOnlyList<string> Categories { get; } = PlaidCategories.Primary;
    public IReadOnlyList<string> MatchTypes { get; } = ["contains", "is exactly"];

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private string pattern = string.Empty;
    [ObservableProperty] private string selectedMatchType = "contains";
    [ObservableProperty] private string? selectedCategory;
    [ObservableProperty] private string minAmountText = string.Empty;
    [ObservableProperty] private string maxAmountText = string.Empty;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        var rules = await api.GetRulesAsync();
        IsLoading = false;
        if (rules is null)
        {
            ErrorMessage = "Couldn't load your rules. Try again.";
            return;
        }
        Rules.Clear();
        foreach (var rule in rules) Rules.Add(new RuleItem(rule));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = StatusMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Pattern) || SelectedCategory is null)
        {
            ErrorMessage = "Enter some text to match and pick a category.";
            return;
        }
        if (!TryAmount(MinAmountText, out var min) || !TryAmount(MaxAmountText, out var max))
        {
            ErrorMessage = "Amounts must be numbers, e.g. 9.99.";
            return;
        }

        var (rule, error) = await api.CreateRuleAsync(new CategoryRuleRequest
        {
            Pattern = Pattern.Trim(),
            MatchType = SelectedMatchType == "is exactly" ? RuleMatchTypes.Equals : RuleMatchTypes.Contains,
            Category = SelectedCategory,
            MinAmount = min,
            MaxAmount = max
        });
        if (rule is null)
        {
            ErrorMessage = error ?? "Couldn't save the rule.";
            return;
        }

        StatusMessage = rule.MatchedTransactions == 1
            ? "Saved. 1 transaction re-categorised."
            : $"Saved. {rule.MatchedTransactions} transactions re-categorised.";
        Pattern = MinAmountText = MaxAmountText = string.Empty;
        SelectedCategory = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(RuleItem item)
    {
        if (!await api.DeleteRuleAsync(item.Id))
        {
            ErrorMessage = "Couldn't delete the rule. Try again.";
            return;
        }
        StatusMessage = "Deleted. Those transactions are back to their original category.";
        await LoadAsync();
    }

    private static bool TryAmount(string text, out decimal? amount)
    {
        amount = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)) return false;
        amount = value;
        return true;
    }
}

public class RuleItem(CategoryRuleResponse rule)
{
    public int Id { get; } = rule.Id;

    public string Summary { get; } =
        $"{(rule.MatchType == RuleMatchTypes.Equals ? "Is exactly" : "Contains")} “{rule.Pattern}” → {Format.Category(rule.Category)}";

    public string Detail { get; } = string.Join(" · ", new[]
    {
        rule.MinAmount is { } min ? $"from {min:C}" : null,
        rule.MaxAmount is { } max ? $"up to {max:C}" : null,
        rule.AccountId is not null ? "one account only" : null,
        rule.MatchedTransactions == 1 ? "1 transaction" : $"{rule.MatchedTransactions} transactions"
    }.Where(s => s is not null));
}
