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
    private bool isLoading;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool hasError;

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
            ErrorMessage = "Couldn't load dashboard data. Pull to refresh to try again.";
            HasError = true;
        }
        else
        {
            NetWorth = summary.NetWorth;
            TotalAssets = summary.TotalAssets;
            TotalLiabilities = summary.TotalLiabilities;
            Income = cashflow.Income;
            Expenses = cashflow.Expenses;
            Net = cashflow.Net;
        }

        IsLoading = false;
    }
}
