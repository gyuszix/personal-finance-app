using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.App.Services;

namespace PersonalFinance.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ApiService _apiService;

    public DashboardViewModel(ApiService apiService)
    {
        _apiService = apiService;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool isLoading;

    // Lets the Refresh button disable itself mid-load without needing a
    // negating value converter.
    public bool IsNotLoading => !IsLoading;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool hasError;

    // A user with no linked accounts would otherwise just see a wall of
    // $0.00, which is indistinguishable from a failed load.
    [ObservableProperty]
    private bool hasNoAccounts;

    [ObservableProperty]
    private bool hasAccounts;

    [ObservableProperty]
    private decimal netWorth;

    [ObservableProperty]
    private decimal totalAssets;

    [ObservableProperty]
    private decimal totalLiabilities;

    [ObservableProperty]
    private decimal income;

    [ObservableProperty]
    private decimal expenses;

    [ObservableProperty]
    private decimal net;

    [RelayCommand]
    public async Task LoadDashboardAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        HasError = false;

        var summaryTask = _apiService.GetAccountsSummaryAsync();
        var cashflowTask = _apiService.GetCashflowAsync();
        await Task.WhenAll(summaryTask, cashflowTask);

        var summary = summaryTask.Result;
        var cashflow = cashflowTask.Result;

        if (summary == null || cashflow == null)
        {
            ErrorMessage = "Couldn't load dashboard data. Tap Refresh to try again.";
            HasError = true;
            HasNoAccounts = false;
            HasAccounts = false;
        }
        else
        {
            NetWorth = summary.NetWorth;
            TotalAssets = summary.TotalAssets;
            TotalLiabilities = summary.TotalLiabilities;
            Income = cashflow.Income;
            Expenses = cashflow.Expenses;
            Net = cashflow.Net;

            HasNoAccounts = summary.Accounts.Count == 0;
            HasAccounts = !HasNoAccounts;
        }

        IsLoading = false;
    }

    [RelayCommand]
    private async Task GoToAccountsAsync()
    {
        await Shell.Current.GoToAsync("//accounts");
    }
}
