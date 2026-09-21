using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Maui.Storage;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.App.Services;

public class ApiService
{
    private const string AccessTokenKey = "access_token";
    private const string RefreshTokenKey = "refresh_token";

    private readonly HttpClient _http;
    private string? _refreshToken;

    // Base URL of your API — change this when you deploy
    private const string BaseUrl = "http://localhost:5140";

    public ApiService()
    {
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    // Loads a previously persisted session on app startup. Returns false if
    // there's nothing stored - caller should send the user to the login page.
    public async Task<bool> TryRestoreSessionAsync()
    {
        var token = await SecureStorage.Default.GetAsync(AccessTokenKey);
        var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);

        if (token == null || refreshToken == null) return false;

        _refreshToken = refreshToken;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return true;
    }

    private async Task SetSessionAsync(string token, string refreshToken)
    {
        _refreshToken = refreshToken;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await SecureStorage.Default.SetAsync(AccessTokenKey, token);
        await SecureStorage.Default.SetAsync(RefreshTokenKey, refreshToken);
    }

    // Revokes the refresh token server-side (best-effort) and drops the local session
    public async Task LogoutAsync()
    {
        if (_refreshToken != null)
        {
            try
            {
                await _http.PostAsJsonAsync("/api/v1/auth/revoke", new { refreshToken = _refreshToken });
            }
            catch (HttpRequestException)
            {
                // Offline logout is still a logout - just drop the local session
            }
        }

        _refreshToken = null;
        _http.DefaultRequestHeaders.Authorization = null;
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
    }

    // Calls a request once; on a 401 (expired access token), refreshes and
    // retries it exactly once. Falls through to the original response if
    // refreshing fails, so callers still get a normal unauthorized result.
    private async Task<HttpResponseMessage> SendWithRefreshAsync(Func<Task<HttpResponseMessage>> send)
    {
        var response = await send();
        if (response.StatusCode != HttpStatusCode.Unauthorized || _refreshToken == null)
            return response;

        if (!await TryRefreshAsync()) return response;

        return await send();
    }

    private async Task<bool> TryRefreshAsync()
    {
        var response = await _http.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = _refreshToken });
        if (!response.IsSuccessStatusCode) return false;

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (result == null) return false;

        await SetSessionAsync(result.Token, result.RefreshToken);
        return true;
    }

    // POST /api/v1/auth/login
    public async Task<string?> LoginAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password
        });

        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (result != null) await SetSessionAsync(result.Token, result.RefreshToken);
        return result?.Token;
    }

    // POST /api/v1/auth/register
    public async Task<bool> RegisterAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password
        });

        return response.IsSuccessStatusCode;
    }

    // GET /api/v1/transactions
    public async Task<PagedResult<TransactionResponse>> GetTransactionsAsync(
        int page = 1, int pageSize = 50, string? category = null)
    {
        var query = $"/api/v1/transactions?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(category)) query += $"&category={Uri.EscapeDataString(category)}";

        var response = await SendWithRefreshAsync(() => _http.GetAsync(query));
        if (!response.IsSuccessStatusCode) return new PagedResult<TransactionResponse>();

        var result = await response.Content.ReadFromJsonAsync<PagedResult<TransactionResponse>>();
        return result ?? new PagedResult<TransactionResponse>();
    }

    // GET /api/v1/transactions/summary - used to populate the category filter
    // with categories the user actually has transactions in
    public async Task<List<TransactionSummaryResponse>> GetTransactionSummaryAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("/api/v1/transactions/summary"));
        if (!response.IsSuccessStatusCode) return [];

        var result = await response.Content.ReadFromJsonAsync<List<TransactionSummaryResponse>>();
        return result ?? [];
    }

    // GET /api/v1/accounts/summary
    public async Task<AccountsSummaryResponse?> GetAccountsSummaryAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("/api/v1/accounts/summary"));
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<AccountsSummaryResponse>();
    }

    // GET /api/v1/transactions/cashflow - current month
    public async Task<CashflowResponse?> GetCashflowAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("/api/v1/transactions/cashflow"));
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<CashflowResponse>();
    }

    // GET /api/v1/plaid/link-token
    public async Task<string?> GetLinkTokenAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.GetAsync("/api/v1/plaid/link-token"));
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<LinkTokenResponse>();
        return result?.LinkToken;
    }

    // POST /api/v1/plaid/exchange-token
    public async Task<bool> ExchangeTokenAsync(string publicToken)
    {
        var response = await SendWithRefreshAsync(() =>
            _http.PostAsJsonAsync("/api/v1/plaid/exchange-token", new { publicToken }));

        return response.IsSuccessStatusCode;
    }

    // POST /api/v1/transactions/sync
    public async Task<bool> SyncTransactionsAsync()
    {
        var response = await SendWithRefreshAsync(() => _http.PostAsync("/api/v1/transactions/sync", null));
        return response.IsSuccessStatusCode;
    }
}

// Response shapes for deserializing API responses
public record TokenResponse(string Token, string RefreshToken);
public record LinkTokenResponse(string LinkToken);
