using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PersonalFinance.App.Insights;
using PersonalFinance.App.Services;

namespace PersonalFinance.App.ViewModels;

// The Dashboard is a set of insight cards grouped into tabs (#85). This
// owns the month, the tabs and the first-run state. Each card loads itself.
public partial class DashboardViewModel : ObservableObject
{
    private readonly ApiService _apiService;
    private readonly IReadOnlyList<InsightCardViewModel> _cards;

    public DashboardViewModel(ApiService apiService, IEnumerable<InsightCardViewModel> cards)
    {
        _apiService = apiService;
        _cards = cards.ToList();

        Tabs = new ObservableCollection<InsightTab>(
            _cards.Select(c => c.Group).Distinct().Order().Select(g => new InsightTab(g)));
        if (Tabs.Count > 0) SelectTab(Tabs[0]);

        // A background sync landed - every card may have moved.
        WeakReferenceMessenger.Default.Register<DashboardViewModel, SyncCompletedMessage>(this,
            (vm, _) => MainThread.BeginInvokeOnMainThread(() => vm.LoadDashboardCommand.Execute(null)));
    }

    public ObservableCollection<InsightTab> Tabs { get; }

    [ObservableProperty]
    private IReadOnlyList<InsightCardViewModel> visibleCards = [];

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

    // A user with no linked accounts would otherwise see a column of empty
    // cards, with nothing pointing at "Connect a bank".
    [ObservableProperty]
    private bool hasNoAccounts;

    [ObservableProperty]
    private bool hasAccounts;

    // First day of the month the cards show. Starts on the current month;
    // the user can step back through history (#63).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthLabel))]
    [NotifyPropertyChangedFor(nameof(CanGoToNextMonth))]
    private DateTime selectedMonth = StartOfMonth(DateTime.Today);

    public string MonthLabel => SelectedMonth == StartOfMonth(DateTime.Today)
        ? "This Month"
        : SelectedMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    // There's nothing in the future to show.
    public bool CanGoToNextMonth => SelectedMonth < StartOfMonth(DateTime.Today);

    [RelayCommand]
    public async Task LoadDashboardAsync()
    {
        IsLoading = true;
        HasError = false;

        var summary = await _apiService.GetAccountsSummaryAsync();
        if (summary == null)
        {
            ErrorMessage = "Couldn't reach the server. Tap Refresh to try again.";
            HasError = true;
            HasNoAccounts = false;
            HasAccounts = false;
        }
        else
        {
            HasNoAccounts = summary.Accounts.Count == 0;
            HasAccounts = !HasNoAccounts;
            if (HasAccounts) await LoadCardsAsync();
        }

        IsLoading = false;
    }

    [RelayCommand]
    private void SelectTab(InsightTab tab)
    {
        foreach (var t in Tabs) t.IsSelected = t == tab;
        VisibleCards = _cards.Where(c => c.Group == tab.Group).ToList();
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        SelectedMonth = SelectedMonth.AddMonths(-1);
        await LoadCardsAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        if (!CanGoToNextMonth) return;
        SelectedMonth = SelectedMonth.AddMonths(1);
        await LoadCardsAsync();
    }

    // Every card, not just the visible tab's, so switching tabs is instant.
    private Task LoadCardsAsync()
    {
        var month = SelectedMonth;
        return Task.WhenAll(_cards.Select(c => c.LoadAsync(month)));
    }

    private static DateTime StartOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    [RelayCommand]
    private async Task GoToAccountsAsync()
    {
        await Shell.Current.GoToAsync("//accounts");
    }
}

public partial class InsightTab(InsightGroup group) : ObservableObject
{
    public InsightGroup Group { get; } = group;
    public string Name { get; } = group.Display();

    [ObservableProperty]
    private bool isSelected;
}
