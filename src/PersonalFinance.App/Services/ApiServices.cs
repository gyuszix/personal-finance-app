using System.Net.Http.Headers;
using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.App.Services;

public class ApiService
{
    private readonly HttpClient _http;
    private string? _token;

    // Base URL of your API — change this when you deploy
    private const string BaseUrl = "http://localhost:5140";

    public ApiService()
    {
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    // Stores the JWT so all subsequent requests send it automatically
    public void SetToken(string token)
    {
        _token = token;
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
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
        if (result?.Token != null) SetToken(result.Token);
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

    // GET /api/v1/transactions - pageSize is large enough to keep today's
    // "load everything at once" UI behavior; proper infinite-scroll paging
    // is GUI work, not covered here
    public async Task<List<TransactionResponse>> GetTransactionsAsync()
    {
        var response = await _http.GetAsync("/api/v1/transactions?pageSize=200");
        if (!response.IsSuccessStatusCode) return [];

        var result = await response.Content.ReadFromJsonAsync<PagedResult<TransactionResponse>>();
        return result?.Items ?? [];
    }

    // GET /api/v1/plaid/link-token
    public async Task<string?> GetLinkTokenAsync()
    {
        var response = await _http.GetAsync("/api/v1/plaid/link-token");
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<LinkTokenResponse>();
        return result?.LinkToken;
    }

    // POST /api/v1/plaid/exchange-token
    public async Task<bool> ExchangeTokenAsync(string publicToken)
    {
        var response = await _http.PostAsJsonAsync("/api/v1/plaid/exchange-token", new
        {
            publicToken
        });

        return response.IsSuccessStatusCode;
    }
}

// Response shapes for deserializing API responses
public record TokenResponse(string Token);
public record LinkTokenResponse(string LinkToken);