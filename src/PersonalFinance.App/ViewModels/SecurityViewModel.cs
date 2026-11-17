using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.App.Services;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.App.ViewModels;

// Two-factor authentication and recent security activity (#117, #116).
// Setup is two steps on purpose - get a key, then prove the authenticator
// app has it - so a mistyped key can't lock the user out.
public partial class SecurityViewModel(ApiService api) : ObservableObject
{
    public ObservableCollection<ActivityItem> Activity { get; } = [];
    public ObservableCollection<string> RecoveryCodes { get; } = [];

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsOff))]
    private bool isEnabled;

    [ObservableProperty] private int recoveryCodesLeft;

    // Setting up: the key is shown until a code confirms it.
    [ObservableProperty] private bool isSettingUp;
    [ObservableProperty] private string sharedKey = string.Empty;
    [ObservableProperty] private string authenticatorUri = string.Empty;

    [ObservableProperty] private bool hasNewRecoveryCodes;
    [ObservableProperty] private string code = string.Empty;

    public bool IsOff => !IsEnabled;

    public string StatusText => IsEnabled
        ? $"On. {RecoveryCodesLeft} recovery codes left."
        : "Off. Your password is the only thing protecting your bank data.";

    partial void OnRecoveryCodesLeftChanged(int value) => OnPropertyChanged(nameof(StatusText));

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var status = await api.GetMfaStatusAsync();
            if (status is null)
            {
                ErrorMessage = "Couldn't load your security settings. Try again.";
                return;
            }
            IsEnabled = status.Enabled;
            RecoveryCodesLeft = status.RecoveryCodesLeft;

            Activity.Clear();
            foreach (var e in await api.GetRecentActivityAsync() ?? [])
                Activity.Add(new ActivityItem(e));
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task StartSetupAsync()
    {
        ErrorMessage = string.Empty;
        var setup = await api.SetupMfaAsync();
        if (setup is null)
        {
            ErrorMessage = "Couldn't start setup. Try again.";
            return;
        }
        SharedKey = setup.SharedKey;
        AuthenticatorUri = setup.AuthenticatorUri;
        Code = string.Empty;
        IsSettingUp = true;
    }

    [RelayCommand]
    private Task CopyKeyAsync() => Clipboard.Default.SetTextAsync(SharedKey.Replace(" ", ""));

    [RelayCommand]
    private Task CopyUriAsync() => Clipboard.Default.SetTextAsync(AuthenticatorUri);

    [RelayCommand]
    private async Task ConfirmSetupAsync()
    {
        var codes = await api.EnableMfaAsync(Code);
        if (codes is null)
        {
            ErrorMessage = "That code didn't match. Check the key in your authenticator app and try the newest code.";
            return;
        }
        IsSettingUp = false;
        SharedKey = AuthenticatorUri = Code = string.Empty;
        ShowRecoveryCodes(codes);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DisableAsync()
    {
        if (!await api.DisableMfaAsync(Code))
        {
            ErrorMessage = "Enter a current authenticator code or an unused recovery code to turn this off.";
            return;
        }
        Code = string.Empty;
        HasNewRecoveryCodes = false;
        RecoveryCodes.Clear();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RegenerateRecoveryCodesAsync()
    {
        var codes = await api.RegenerateRecoveryCodesAsync(Code);
        if (codes is null)
        {
            ErrorMessage = "Enter a current authenticator code to get new recovery codes.";
            return;
        }
        Code = string.Empty;
        ShowRecoveryCodes(codes);
        await LoadAsync();
    }

    [RelayCommand]
    private Task CopyRecoveryCodesAsync() => Clipboard.Default.SetTextAsync(string.Join(Environment.NewLine, RecoveryCodes));

    [RelayCommand]
    private void DismissRecoveryCodes()
    {
        RecoveryCodes.Clear();
        HasNewRecoveryCodes = false;
    }

    private void ShowRecoveryCodes(IEnumerable<string> codes)
    {
        ErrorMessage = string.Empty;
        RecoveryCodes.Clear();
        foreach (var c in codes) RecoveryCodes.Add(c);
        HasNewRecoveryCodes = true;
    }
}

public class ActivityItem(AuditEventResponse e)
{
    public string Title { get; } = e.EventType switch
    {
        "auth.login.succeeded" => "Signed in",
        "auth.login.failed" => "Failed sign-in attempt",
        "auth.refresh.reuse_detected" => "Session ended: possible stolen token",
        "auth.logout" => "Signed out",
        "auth.mfa.enabled" => "Two-factor authentication turned on",
        "auth.mfa.disabled" => "Two-factor authentication turned off",
        "auth.mfa.failed" => "Wrong two-factor code",
        "auth.mfa.recovery_code_used" => "Signed in with a recovery code",
        "auth.mfa.recovery_codes_regenerated" => "New recovery codes created",
        "plaid.item.linked" => "Bank connected",
        "plaid.item.unlinked" => "Bank disconnected",
        _ => e.EventType
    };

    public string When { get; } = e.OccurredAt.ToLocalTime().ToString("g");
    public string Where { get; } = string.Join(" · ", new[] { e.IpAddress, e.UserAgent }.Where(s => !string.IsNullOrEmpty(s)));

    // Worth a second look: the things an account owner should notice.
    public bool IsWarning { get; } = e.EventType is "auth.login.failed" or "auth.refresh.reuse_detected"
        or "auth.mfa.failed" or "auth.mfa.disabled";
}
