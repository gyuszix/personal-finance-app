using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.App.Services;

namespace PersonalFinance.App.ViewModels;

// Placeholder shell for the Accounts tab - linked-accounts list is tracked separately.
public partial class AccountsViewModel : ObservableObject
{
    private readonly ApiService _apiService;

    public AccountsViewModel(ApiService apiService)
    {
        _apiService = apiService;
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        _apiService.Logout();
        await Shell.Current.GoToAsync("//login");
    }
}
