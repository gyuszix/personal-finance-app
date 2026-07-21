using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.App.Services;

namespace PersonalFinance.App.ViewModels;

// I have never seen a partial class before,
//turns out it's a mechanism where the source generator injects the boilerplate property ode into the class 
public partial class LoginViewModel : ObservableObject
{
    private readonly ApiService _apiService;

    public LoginViewModel(ApiService apiService)
    {
        _apiService = apiService;
    }

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        var token = await _apiService.LoginAsync(Email, Password);

        if (token != null)
        {
            // Navigate to transactions page on success
            await Shell.Current.GoToAsync("//transactions");
        }
        else
        {
            ErrorMessage = "Invalid email or password";
        }

        IsLoading = false;
    }
}