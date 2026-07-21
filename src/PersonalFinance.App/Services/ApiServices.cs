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

    // POST /auth/login
    public async Task<string?> LoginAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("/auth/login", new
        {
            email,
            password
        });

        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (result?.Token != null) SetToken(result.Token);
        return result?.Token;
    }

    // POST /auth/register
    public async Task<bool> RegisterAsync(string email, string password)
    {
        var response = await _http.PostAsJsonAsync("/auth/register", new
        {
            email,
            password
        });

        return response.IsSuccessStatusCode;
    }

    // GET /transactions
    public async Task<List<TransactionResponse>> GetTransactionsAsync()
    {
        var response = await _http.GetAsync("/transactions");
        if (!response.IsSuccessStatusCode) return [];

        return await response.Content.ReadFromJsonAsync<List<TransactionResponse>>()
               ?? [];
    }

    // GET /plaid/link-token
    public async Task<string?> GetLinkTokenAsync()
    {
        var response = await _http.GetAsync("/plaid/link-token");
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<LinkTokenResponse>();
        return result?.LinkToken;
    }

    // POST /plaid/exchange-token
    public async Task<bool> ExchangeTokenAsync(string publicToken)
    {
        var response = await _http.PostAsJsonAsync("/plaid/exchange-token", new
        {
            publicToken
        });

        return response.IsSuccessStatusCode;
    }
}

// Response shapes for deserializing API responses
public record TokenResponse(string Token);
public record LinkTokenResponse(string LinkToken);