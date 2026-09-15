using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.App.Services;
using PersonalFinance.Shared.DTOs;
using System.Collections.ObjectModel;

namespace PersonalFinance.App.ViewModels;

public partial class TransactionsViewModel : ObservableObject
{
    private const string AllCategoriesOption = "All categories";
    private const int PageSize = 50;

    private readonly ApiService _apiService;
    private int _currentPage;

    public TransactionsViewModel(ApiService apiService)
    {
        _apiService = apiService;
    }

    [ObservableProperty]
    private ObservableCollection<TransactionResponse> transactions = [];

    [ObservableProperty]
    private ObservableCollection<string> categories = [AllCategoriesOption];

    [ObservableProperty]
    private string selectedCategory = AllCategoriesOption;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isLoadingMore;

    [ObservableProperty]
    private bool hasMore;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    // Reloads from page 1 whenever the filter changes - the category picker
    // is the only thing that should reset pagination state.
    partial void OnSelectedCategoryChanged(string value) => _ = LoadFirstPageAsync();

    [RelayCommand]
    public async Task LoadTransactionsAsync()
    {
        await LoadCategoriesAsync();
        await LoadFirstPageAsync();
    }

    private async Task LoadFirstPageAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        _currentPage = 1;

        var category = SelectedCategory == AllCategoriesOption ? null : SelectedCategory;
        var result = await _apiService.GetTransactionsAsync(_currentPage, PageSize, category);

        Transactions = new ObservableCollection<TransactionResponse>(result.Items);
        HasMore = result.HasMore;

        IsLoading = false;
    }

    [RelayCommand]
    public async Task LoadMoreTransactionsAsync()
    {
        if (IsLoading || IsLoadingMore || !HasMore) return;

        IsLoadingMore = true;

        var category = SelectedCategory == AllCategoriesOption ? null : SelectedCategory;
        var result = await _apiService.GetTransactionsAsync(_currentPage + 1, PageSize, category);

        foreach (var transaction in result.Items) Transactions.Add(transaction);
        _currentPage = result.Page;
        HasMore = result.HasMore;

        IsLoadingMore = false;
    }

    private async Task LoadCategoriesAsync()
    {
        var summary = await _apiService.GetTransactionSummaryAsync();

        Categories = new ObservableCollection<string>(
            new[] { AllCategoriesOption }.Concat(summary.Select(s => s.Category)));
    }
}
