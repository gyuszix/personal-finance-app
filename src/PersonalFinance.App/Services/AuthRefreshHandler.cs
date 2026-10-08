using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PersonalFinance.App.Services;

// Attaches the bearer token to every request made through IPersonalFinanceApi,
// and on a 401 (expired access token) refreshes and retries once - callers
// never see the 401 unless the refresh itself fails. Talks to /auth/refresh
// through its own plain HttpClient rather than IPersonalFinanceApi, since
// that would mean this handler depending on the very client it's attached to.
public class AuthRefreshHandler(AuthTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sentAccessToken = tokenProvider.AccessToken;
        Attach(request);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || tokenProvider.RefreshToken == null)
            return response;

        // Goes through the provider so concurrent 401s share one refresh -
        // two refreshes with the same token trip the server's reuse
        // detection and revoke the whole session (#54).
        if (!await tokenProvider.RefreshAsync(sentAccessToken, RequestRefreshAsync, cancellationToken))
            return response;

        var retry = await CloneAsync(request);
        Attach(retry);
        return await base.SendAsync(retry, cancellationToken);
    }

    private void Attach(HttpRequestMessage request)
    {
        if (tokenProvider.AccessToken != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.AccessToken);
    }

    private static async Task<TokenResponse?> RequestRefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiConfig.BaseUrl) };
        var response = await http.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(refreshToken),
            cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content != null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.Add(header.Key, header.Value);
        }

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }
}
