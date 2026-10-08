using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PersonalFinance.App.Services;
using PersonalFinance.Shared.DTOs;
using System.Collections.ObjectModel;

namespace PersonalFinance.App.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    private readonly ApiService _apiService;
    private readonly IPlaidLinkService _plaidLinkService;

    public AccountsViewModel(ApiService apiService, IPlaidLinkService plaidLinkService)
    {
        _apiService = apiService;
        _plaidLinkService = plaidLinkService;

        // A background sync landed - balances may have moved.
        WeakReferenceMessenger.Default.Register<AccountsViewModel, SyncCompletedMessage>(this,
            (vm, _) => MainThread.BeginInvokeOnMainThread(() => vm.LoadAccountsCommand.Execute(null)));
    }

    [ObservableProperty]
    private ObservableCollection<AccountResponse> accounts = [];

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

    [ObservableProperty]
    private bool isEmpty;

    // Explicit inverse of IsEmpty so the list and the empty-state message can
    // never both be visible in the row they share.
    [ObservableProperty]
    private bool hasAccounts;

    [ObservableProperty]
    private bool isConnectingBank;

    [ObservableProperty]
    private bool canConnectBank = true;

    [ObservableProperty]
    private string connectBankStatus = string.Empty;

    [ObservableProperty]
    private bool hasConnectBankStatus;

    // The per-account breakdown already comes back on /accounts/summary
    // alongside the aggregates the Dashboard uses - no new endpoint needed.
    [RelayCommand]
    public async Task LoadAccountsAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        HasError = false;

        var summary = await _apiService.GetAccountsSummaryAsync();

        if (summary == null)
        {
            ErrorMessage = "Couldn't load your accounts. Tap Refresh to try again.";
            HasError = true;
            IsEmpty = false;
            HasAccounts = false;
        }
        else
        {
            Accounts = new ObservableCollection<AccountResponse>(summary.Accounts);
            IsEmpty = Accounts.Count == 0;
            HasAccounts = !IsEmpty;
        }

        IsLoading = false;
    }

    [RelayCommand]
    private async Task ConnectBankAsync()
    {
        IsConnectingBank = true;
        CanConnectBank = false;
        ConnectBankStatus = string.Empty;
        HasConnectBankStatus = false;

        var linkToken = await _apiService.GetLinkTokenAsync();
        if (linkToken == null)
        {
            SetStatus("Couldn't start bank connection. Please try again.");
            return;
        }

        string? publicToken;
        try
        {
            publicToken = await _plaidLinkService.OpenLinkAsync(linkToken);
        }
        catch (Exception)
        {
            SetStatus("Something went wrong connecting your bank. Please try again.");
            return;
        }

        if (publicToken == null)
        {
            SetStatus("Bank connection cancelled.");
            return;
        }

        var exchanged = await _apiService.ExchangeTokenAsync(publicToken);

        if (!exchanged)
        {
            SetStatus("Couldn't link your bank account. Please try again.");
            return;
        }

        // Kick off a sync right away so transactions show up immediately
        // instead of waiting for the up-to-30-minute background sync.
        var synced = await _apiService.SyncTransactionsAsync();

        // Pull the newly linked accounts into the list behind the user before
        // we navigate away, so coming back to this tab shows them already.
        await LoadAccountsAsync();

        // Linking worked either way; a failed sync just means transactions
        // arrive with the next scheduled sync instead of right now.
        SetStatus(synced
            ? "Bank connected!"
            : "Bank connected! Transactions will appear after the next sync.");
        await Shell.Current.GoToAsync("//dashboard");
    }

    private void SetStatus(string message)
    {
        ConnectBankStatus = message;
        HasConnectBankStatus = true;
        IsConnectingBank = false;
        CanConnectBank = true;
    }

    [RelayCommand]
    private async Task UnlinkAccountAsync(AccountResponse account)
    {
        // Plaid can only disconnect a whole bank connection, so be explicit
        // that sibling accounts from the same bank go too.
        var confirmed = await Shell.Current.DisplayAlertAsync(
            $"Unlink {account.BankName}?",
            "This disconnects the bank and removes every account it brought in, along with their transactions. You can connect it again later.",
            "Unlink",
            "Cancel");
        if (!confirmed) return;

        if (!await _apiService.UnlinkAccountAsync(account.AccountId))
        {
            ErrorMessage = $"Couldn't unlink {account.BankName}. Please try again.";
            HasError = true;
            return;
        }

        await LoadAccountsAsync();
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _apiService.LogoutAsync();
        await Shell.Current.GoToAsync("//login");
    }
}
