using PersonalFinance.Shared.DTOs;
using Refit;

namespace PersonalFinance.App.Services;

public interface IPersonalFinanceApi
{
    [Post("/api/v1/auth/login")]
    Task<ApiResponse<TokenResponse>> LoginAsync([Body] LoginRequest request);

    [Post("/api/v1/auth/register")]
    Task<IApiResponse> RegisterAsync([Body] RegisterRequest request);

    [Post("/api/v1/auth/refresh")]
    Task<ApiResponse<TokenResponse>> RefreshAsync([Body] RefreshTokenRequest request);

    [Post("/api/v1/auth/revoke")]
    Task<IApiResponse> RevokeAsync([Body] RefreshTokenRequest request);

    [Get("/api/v1/transactions")]
    Task<ApiResponse<PagedResult<TransactionResponse>>> GetTransactionsAsync(
        [Query] int page, [Query] int pageSize, [Query] string? category);

    [Get("/api/v1/transactions/summary")]
    Task<ApiResponse<List<TransactionSummaryResponse>>> GetTransactionSummaryAsync();

    [Get("/api/v1/accounts/summary")]
    Task<ApiResponse<AccountsSummaryResponse>> GetAccountsSummaryAsync();

    [Get("/api/v1/transactions/cashflow")]
    Task<ApiResponse<CashflowResponse>> GetCashflowAsync();

    [Get("/api/v1/plaid/link-token")]
    Task<ApiResponse<LinkTokenResponse>> GetLinkTokenAsync();

    [Post("/api/v1/plaid/exchange-token")]
    Task<IApiResponse> ExchangeTokenAsync([Body] ExchangeTokenRequest request);

    [Post("/api/v1/transactions/sync")]
    Task<IApiResponse> SyncTransactionsAsync();
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password);
public record RefreshTokenRequest(string RefreshToken);
public record ExchangeTokenRequest(string PublicToken);

public record TokenResponse(string Token, string RefreshToken);
public record LinkTokenResponse(string LinkToken);
