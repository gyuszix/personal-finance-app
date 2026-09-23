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
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool isLoading;

    // Lets the Refresh button disable itself mid-load without needing a
    // negating value converter.
    public bool IsNotLoading => !IsLoading;

    [ObservableProperty]
    private bool isLoadingMore;

    [ObservableProperty]
    private bool hasMore;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private bool isOffline;

    [ObservableProperty]
    private string offlineStatusText = string.Empty;

    [ObservableProperty]
    private bool isEmpty;

    // Explicit inverse of IsEmpty so the list and the empty-state message can
    // never both be visible in the row they share.
    [ObservableProperty]
    private bool hasTransactions;

    [ObservableProperty]
    private string emptyMessage = string.Empty;

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
        HasError = false;
        IsOffline = false;
        IsEmpty = false;
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

        UpdateEmptyState(category);

        IsLoading = false;
    }

    // An empty list means something different depending on how we got here,
    // and a blank screen doesn't tell the user which case they're in.
    private void UpdateEmptyState(string? category)
    {
        IsEmpty = Transactions.Count == 0;
        HasTransactions = !IsEmpty;
        if (!IsEmpty) return;

        EmptyMessage = IsOffline
            ? "No cached transactions to show while offline."
            : category != null
                ? $"No transactions in {category}."
                : "No transactions yet. Connect a bank on the Accounts tab to import them.";
    }

    [RelayCommand]
    public async Task LoadMoreTransactionsAsync()
    {
        if (IsLoading || IsLoadingMore || !HasMore) return;

        IsLoadingMore = true;

        var category = SelectedCategory == AllCategoriesOption ? null : SelectedCategory;

        try
        {
            var result = await _apiService.GetTransactionsAsync(_currentPage + 1, PageSize, category);

            foreach (var transaction in result.Items) Transactions.Add(transaction);
            _currentPage = result.Page;
            HasMore = result.HasMore;
        }
        catch (Exception)
        {
            // Losing the connection mid-scroll shouldn't strand the pager.
            // Stop requesting further pages and let the user retry via Refresh.
            HasMore = false;
            ErrorMessage = "Couldn't load more transactions. Tap Refresh to try again.";
            HasError = true;
        }
        finally
        {
            // Must be in a finally: if this flag is left set, the guard at the
            // top of this method blocks paging for the rest of the session.
            IsLoadingMore = false;
        }
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
