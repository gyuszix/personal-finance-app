using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PersonalFinance.App.Services;

namespace PersonalFinance.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ApiService _apiService;

    public DashboardViewModel(ApiService apiService)
    {
        _apiService = apiService;

        // A background sync landed - balances and cashflow may have moved.
        WeakReferenceMessenger.Default.Register<DashboardViewModel, SyncCompletedMessage>(this,
            (vm, _) => MainThread.BeginInvokeOnMainThread(() => vm.LoadDashboardCommand.Execute(null)));
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

    // First day of the month the cash flow section shows. Starts on the
    // current month; the user can step back through history (#63).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthLabel))]
    [NotifyPropertyChangedFor(nameof(CanGoToNextMonth))]
    private DateTime selectedMonth = StartOfMonth(DateTime.Today);

    public string MonthLabel => SelectedMonth == StartOfMonth(DateTime.Today)
        ? "This Month"
        : SelectedMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    // There's no cash flow in the future to show.
    public bool CanGoToNextMonth => SelectedMonth < StartOfMonth(DateTime.Today);

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
        var cashflowTask = _apiService.GetCashflowAsync(SelectedMonth);
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
    private async Task PreviousMonthAsync()
    {
        SelectedMonth = SelectedMonth.AddMonths(-1);
        await LoadCashflowAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        if (!CanGoToNextMonth) return;
        SelectedMonth = SelectedMonth.AddMonths(1);
        await LoadCashflowAsync();
    }

    // Changing month only changes the cash flow - no need to reload balances.
    private async Task LoadCashflowAsync()
    {
        var month = SelectedMonth;
        var cashflow = await _apiService.GetCashflowAsync(month);

        // The user may have stepped again while this was in flight.
        if (month != SelectedMonth) return;

        if (cashflow == null)
        {
            ErrorMessage = "Couldn't load that month. Tap Refresh to try again.";
            HasError = true;
            return;
        }

        HasError = false;
        Income = cashflow.Income;
        Expenses = cashflow.Expenses;
        Net = cashflow.Net;
    }

    private static DateTime StartOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    [RelayCommand]
    private async Task GoToAccountsAsync()
    {
        await Shell.Current.GoToAsync("//accounts");
    }
}
