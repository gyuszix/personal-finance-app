using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.App.Services;

namespace PersonalFinance.App.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    private readonly ApiService _apiService;
    private readonly IPlaidLinkService _plaidLinkService;

    public AccountsViewModel(ApiService apiService, IPlaidLinkService plaidLinkService)
    {
        _apiService = apiService;
        _plaidLinkService = plaidLinkService;
    }

    [ObservableProperty]
    private bool isConnectingBank;

    [ObservableProperty]
    private bool canConnectBank = true;

    [ObservableProperty]
    private string connectBankStatus = string.Empty;

    [ObservableProperty]
    private bool hasConnectBankStatus;

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
        await _apiService.SyncTransactionsAsync();

        SetStatus("Bank connected!");
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
    private async Task LogoutAsync()
    {
        _apiService.Logout();
        await Shell.Current.GoToAsync("//login");
    }
}
