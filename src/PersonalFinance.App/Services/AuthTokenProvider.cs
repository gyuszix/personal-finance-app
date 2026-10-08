using Microsoft.Maui.Storage;

namespace PersonalFinance.App.Services;

// Single source of truth for the current session - in-memory token access
// for AuthRefreshHandler and ApiService, backed by SecureStorage so it
// survives an app restart. Persistence failures (e.g. Keychain access
// issues) are swallowed - the session still works for this run, it just
// won't survive a restart.
public class AuthTokenProvider
{
    private const string AccessTokenKey = "access_token";
    private const string RefreshTokenKey = "refresh_token";

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // Refreshes the session at most once per expired access token, however
    // many requests got a 401 for it at the same time. The server rotates the
    // refresh token on use and treats a second use as theft - revoking every
    // session for the user - so concurrent callers must share one refresh.
    // rejectedAccessToken is the token the caller's request was sent with:
    // if it's already been replaced, another caller refreshed while this one
    // waited, and the caller can just retry with the new token.
    public async Task<bool> RefreshAsync(
        string? rejectedAccessToken,
        Func<string, CancellationToken, Task<TokenResponse?>> requestRefresh,
        CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (AccessToken != null && AccessToken != rejectedAccessToken) return true;
            if (RefreshToken == null) return false;

            var result = await requestRefresh(RefreshToken, cancellationToken);
            if (result == null) return false;

            await SetSessionAsync(result.Token, result.RefreshToken);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<bool> TryRestoreAsync()
    {
        string? token, refreshToken;
        try
        {
            token = await SecureStorage.Default.GetAsync(AccessTokenKey);
            refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SecureStorage] Failed to restore session: {ex}");
            return false;
        }

        if (token == null || refreshToken == null) return false;

        AccessToken = token;
        RefreshToken = refreshToken;
        return true;
    }

    public async Task SetSessionAsync(string token, string refreshToken)
    {
        AccessToken = token;
        RefreshToken = refreshToken;

        try
        {
            await SecureStorage.Default.SetAsync(AccessTokenKey, token);
            await SecureStorage.Default.SetAsync(RefreshTokenKey, refreshToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SecureStorage] Failed to persist session: {ex}");
        }
    }

    public void Clear()
    {
        AccessToken = null;
        RefreshToken = null;
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
    }
}
