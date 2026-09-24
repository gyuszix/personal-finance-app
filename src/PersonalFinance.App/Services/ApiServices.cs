using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.App.Services;

public class ApiService(IPersonalFinanceApi api, AuthTokenProvider tokenProvider)
{
    // Loads a previously persisted session on app startup. Returns false if
    // there's nothing stored - caller should send the user to the login page.
    public Task<bool> TryRestoreSessionAsync() => tokenProvider.TryRestoreAsync();

    // Revokes the refresh token server-side (best-effort) and drops the local session
    public async Task LogoutAsync()
    {
        if (tokenProvider.RefreshToken != null)
        {
            try
            {
                await api.RevokeAsync(new RefreshTokenRequest(tokenProvider.RefreshToken));
            }
            catch (HttpRequestException)
            {
                // Offline logout is still a logout - just drop the local session
            }
        }

        tokenProvider.Clear();
    }

    public async Task<string?> LoginAsync(string email, string password)
    {
        var response = await api.LoginAsync(new LoginRequest(email, password));
        if (!response.IsSuccessStatusCode || response.Content == null) return null;

        await tokenProvider.SetSessionAsync(response.Content.Token, response.Content.RefreshToken);
        return response.Content.Token;
    }

    public async Task<(bool Success, string? Error)> RegisterAsync(string email, string password)
    {
        var response = await api.RegisterAsync(new RegisterRequest(email, password));
        if (response.IsSuccessStatusCode)
            return (true, null);

        List<IdentityErrorDto>? errors = null;
        if (response.Error is Refit.ApiException apiException)
        {
            errors = await apiException.GetContentAsAsync<List<IdentityErrorDto>>();
        }

        var message = errors != null && errors.Count > 0
            ? string.Join(" ", errors.Select(e => e.Description))
            : "Registration failed.";

        return (false, message);
    }

    public async Task<PagedResult<TransactionResponse>> GetTransactionsAsync(
        int page = 1, int pageSize = 50, string? category = null)
    {
        var response = await api.GetTransactionsAsync(page, pageSize, category);
        return response.Content ?? new PagedResult<TransactionResponse>();
    }

    // Used to populate the category filter with categories the user actually has transactions in
    public async Task<List<TransactionSummaryResponse>> GetTransactionSummaryAsync()
    {
        var response = await api.GetTransactionSummaryAsync();
        return response.Content ?? [];
    }

    public async Task<AccountsSummaryResponse?> GetAccountsSummaryAsync()
    {
        var response = await api.GetAccountsSummaryAsync();
        return response.Content;
    }

    // Current month
    public async Task<CashflowResponse?> GetCashflowAsync()
    {
        var response = await api.GetCashflowAsync();
        return response.Content;
    }

    public async Task<string?> GetLinkTokenAsync()
    {
        var response = await api.GetLinkTokenAsync();
        return response.Content?.LinkToken;
    }

    public async Task<bool> ExchangeTokenAsync(string publicToken)
    {
        var response = await api.ExchangeTokenAsync(new ExchangeTokenRequest(publicToken));
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SyncTransactionsAsync()
    {
        var response = await api.SyncTransactionsAsync();
        return response.IsSuccessStatusCode;
    }
}
