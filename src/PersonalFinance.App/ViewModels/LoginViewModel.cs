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

    // Shown once the server says the password was right but the account has
    // two-factor authentication on (#117).
    [ObservableProperty]
    private bool isTwoFactorRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodePlaceholder), nameof(SwitchCodeText))]
    private bool useRecoveryCode;

    [ObservableProperty]
    private string twoFactorCode = string.Empty;

    public string CodePlaceholder => UseRecoveryCode ? "Recovery code (XXXXX-XXXXX)" : "6-digit code from your authenticator app";
    public string SwitchCodeText => UseRecoveryCode ? "Use an authenticator code" : "Use a recovery code instead";

    [RelayCommand]
    private void SwitchCode()
    {
        UseRecoveryCode = !UseRecoveryCode;
        TwoFactorCode = string.Empty;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        var code = IsTwoFactorRequired && !UseRecoveryCode ? TwoFactorCode : null;
        var recovery = IsTwoFactorRequired && UseRecoveryCode ? TwoFactorCode : null;
        var outcome = await _apiService.LoginAsync(Email, Password, code, recovery);

        switch (outcome)
        {
            case LoginOutcome.Success:
                IsTwoFactorRequired = false;
                TwoFactorCode = string.Empty;
                // Navigate into the tabbed main app on success
                await Shell.Current.GoToAsync("//dashboard");
                break;
            case LoginOutcome.RequiresTwoFactor:
                IsTwoFactorRequired = true;
                ErrorMessage = "Enter the code from your authenticator app.";
                break;
            case LoginOutcome.InvalidTwoFactorCode:
                ErrorMessage = "That code didn't work. Codes change every 30 seconds, and each one works once.";
                break;
            default:
                // Also what a locked-out account gets: no hint for attackers.
                ErrorMessage = "Invalid email or password";
                break;
        }

        IsLoading = false;
    }

    [RelayCommand]
    private async Task GoToRegisterAsync()
    {
        await Shell.Current.GoToAsync("//register");
    }
}