using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.App.Services;

namespace PersonalFinance.App.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly ApiService _apiService;

    public RegisterViewModel(ApiService apiService)
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
    private async Task RegisterAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        var success = await _apiService.RegisterAsync(Email, Password);

        if (success)
            await Shell.Current.GoToAsync("//login");
        else
            ErrorMessage = "Registration failed. Try a different email.";

        IsLoading = false;
    }

    [RelayCommand]
    private async Task GoToLoginAsync()
    {
        await Shell.Current.GoToAsync("//login");
    }
}