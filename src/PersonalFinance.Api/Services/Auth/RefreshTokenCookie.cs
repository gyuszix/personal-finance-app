namespace PersonalFinance.Api.Services.Auth;

// Browser clients keep the refresh token in a cookie JavaScript can't read,
// so an XSS bug can't steal it. Native clients (the MAUI app) get it in the
// response body and keep it in the OS keychain instead - the RFC 8252
// pattern - since there's no page script to protect it from (#115).
public static class RefreshTokenCookie
{
    public const string Name = "pf_refresh";

    // Only ever sent to the endpoints that consume it, never to the rest of
    // the API.
    public const string Path = "/api/v1/auth";

    public static void Set(HttpResponse response, string refreshToken, DateTime expiresAt) =>
        response.Cookies.Append(Name, refreshToken, Options(expiresAt));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(expires: null));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private static CookieOptions Options(DateTime? expires) => new()
    {
        HttpOnly = true,
        // Browsers treat http://localhost as a secure context, so this still
        // works in local development.
        Secure = true,
        // Never sent on cross-site requests, so another site can't make the
        // browser spend it (CSRF).
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires
    };
}
