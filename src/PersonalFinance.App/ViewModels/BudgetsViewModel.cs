using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.App.Insights;
using PersonalFinance.App.Services;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.App.ViewModels;

// Review and set monthly limits per category (#88). A category without a
// saved limit shows its suggestion; saving makes it yours, "Use suggestion"
// goes back.
public partial class BudgetsViewModel(ApiService api) : ObservableObject
{
    // Matches the API's accepted range.
    public const decimal MaxLimit = 1_000_000m;

    public ObservableCollection<BudgetItem> Items { get; } = [];

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool hasError;
    [ObservableProperty] private string errorMessage = string.Empty;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        var budgets = await api.GetBudgetsAsync();
        IsLoading = false;

        if (budgets == null)
        {
            ShowError("Couldn't load your budgets. Try again.");
            return;
        }

        Items.Clear();
        foreach (var budget in budgets) Items.Add(new BudgetItem(budget));
    }

    [RelayCommand]
    private async Task SaveAsync(BudgetItem item)
    {
        if (!decimal.TryParse(item.LimitText, NumberStyles.Number, CultureInfo.CurrentCulture, out var limit)
            || limit < 0 || limit > MaxLimit)
        {
            item.Error = $"Enter an amount from 0 to {MaxLimit.ToString("N0", CultureInfo.CurrentCulture)}.";
            return;
        }

        item.Error = string.Empty;
        if (await api.SetBudgetAsync(item.Category, limit))
            item.MarkSaved(Math.Round(limit, 2, MidpointRounding.AwayFromZero));
        else
            item.Error = "Couldn't save. Try again.";
    }

    [RelayCommand]
    private async Task UseSuggestionAsync(BudgetItem item)
    {
        item.Error = string.Empty;
        if (await api.ClearBudgetAsync(item.Category))
            item.MarkSuggested();
        else
            item.Error = "Couldn't reset. Try again.";
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}

public partial class BudgetItem : ObservableObject
{
    public BudgetItem(BudgetResponse budget)
    {
        Category = budget.Category;
        Name = Format.Category(budget.Category);
        SuggestedLimit = budget.SuggestedLimit;
        isSuggested = budget.IsSuggested;
        limitText = budget.MonthlyLimit.ToString("0.##", CultureInfo.CurrentCulture);
    }

    public string Category { get; }
    public string Name { get; }
    public decimal SuggestedLimit { get; }
    public string SuggestionText => $"Suggested {Format.Money(SuggestedLimit)} from your last three months";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(IsSaved))]
    private bool isSuggested;

    public bool IsSaved => !IsSuggested;

    [ObservableProperty] private string limitText;
    [ObservableProperty] private string error = string.Empty;

    public string Status => IsSuggested ? "Suggested" : "Your limit";

    public void MarkSaved(decimal limit)
    {
        LimitText = limit.ToString("0.##", CultureInfo.CurrentCulture);
        IsSuggested = false;
    }

    public void MarkSuggested()
    {
        LimitText = SuggestedLimit.ToString("0.##", CultureInfo.CurrentCulture);
        IsSuggested = true;
    }
}
