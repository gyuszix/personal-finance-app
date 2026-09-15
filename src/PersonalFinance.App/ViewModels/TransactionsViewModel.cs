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
    private readonly TransactionCacheService _cacheService;
    private int _currentPage;

    public TransactionsViewModel(ApiService apiService, TransactionCacheService cacheService)
    {
        _apiService = apiService;
        _cacheService = cacheService;
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

    [ObservableProperty]
    private bool isOffline;

    [ObservableProperty]
    private string offlineStatusText = string.Empty;

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
        IsOffline = false;
        _currentPage = 1;

        var category = SelectedCategory == AllCategoriesOption ? null : SelectedCategory;

        try
        {
            var result = await _apiService.GetTransactionsAsync(_currentPage, PageSize, category);
            Transactions = new ObservableCollection<TransactionResponse>(result.Items);
            HasMore = result.HasMore;

            // Only the unfiltered first page is cached - it's the most useful
            // "last known" snapshot to fall back to when offline.
            if (category == null)
            {
                await _cacheService.ReplaceTransactionsAsync(result.Items);
            }
        }
        catch (Exception)
        {
            var cached = await _cacheService.GetCachedTransactionsAsync();
            var lastSyncedAt = await _cacheService.GetLastSyncedAtAsync();

            Transactions = new ObservableCollection<TransactionResponse>(cached);
            HasMore = false;
            IsOffline = true;
            OfflineStatusText = lastSyncedAt.HasValue
                ? $"Offline - showing transactions from {lastSyncedAt.Value.ToLocalTime():g}"
                : "Offline - no cached transactions available";
        }

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
        try
        {
            var summary = await _apiService.GetTransactionSummaryAsync();
            Categories = new ObservableCollection<string>(
                new[] { AllCategoriesOption }.Concat(summary.Select(s => s.Category)));
        }
        catch (Exception)
        {
            // Offline - the filter picker just keeps whatever categories it already had.
        }
    }
}
