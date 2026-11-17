using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Services.Auth;

namespace PersonalFinance.Tests;

// Short-lived access tokens, hashed refresh tokens rotating within a
// family, and the HttpOnly cookie for browser clients (#115).
public class TokenHardeningTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private record Tokens(string Token, string? RefreshToken, int ExpiresIn);

    private async Task<(HttpClient Client, string Email)> RegisteredAsync(bool handleCookies = false)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = handleCookies,
            BaseAddress = new Uri("https://localhost") // Secure cookies only travel over https
        });
        var email = $"{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "Test123!" })).EnsureSuccessStatusCode();
        return (client, email);
    }

    private static async Task<Tokens> LoginAsync(HttpClient client, string email, bool cookies = false)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/auth/login{(cookies ? "?useCookies=true" : "")}",
            new { email, password = "Test123!" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string? refreshToken) =>
        client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });

    [Fact]
    public async Task AccessToken_LivesTenMinutes()
    {
        var (client, email) = await RegisteredAsync();
        var tokens = await LoginAsync(client, email);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.Token);
        Assert.Equal(TimeSpan.FromMinutes(10), jwt.ValidTo - jwt.ValidFrom);
        Assert.Equal(600, tokens.ExpiresIn);
        Assert.False(string.IsNullOrEmpty(jwt.Id)); // jti
    }

    [Fact]
    public async Task RefreshTokens_AreStoredOnlyAsHashes()
    {
        var (client, email) = await RegisteredAsync();
        var tokens = await LoginAsync(client, email);

        using var scope = factory.SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.RefreshTokens.SingleAsync(r => r.TokenHash == TokenService.Hash(tokens.RefreshToken!));
        Assert.NotEqual(tokens.RefreshToken, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.False(await db.RefreshTokens.AnyAsync(r => r.TokenHash == tokens.RefreshToken));
    }

    [Fact]
    public async Task Reuse_RevokesOnlyThatFamily_NotTheUsersOtherSessions()
    {
        var (client, email) = await RegisteredAsync();
        var phone = await LoginAsync(client, email);
        var laptop = await LoginAsync(client, email);

        var rotated = await (await RefreshAsync(client, phone.RefreshToken)).Content.ReadFromJsonAsync<Tokens>();
        // Someone replays the phone's old token: theft. The phone's session ends...
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, phone.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, rotated!.RefreshToken)).StatusCode);
        // ...the laptop's doesn't.
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, laptop.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefreshes_WithOneToken_LetAtMostOneThrough()
    {
        var (client, email) = await RegisteredAsync();
        var tokens = await LoginAsync(client, email);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshAsync(client, tokens.RefreshToken)));

        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) <= 1);
        using var scope = factory.SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var family = (await db.RefreshTokens.SingleAsync(r => r.TokenHash == TokenService.Hash(tokens.RefreshToken!))).FamilyId;
        Assert.True(await db.RefreshTokens.CountAsync(r => r.FamilyId == family && r.RevokedAt == null) <= 1);
    }

    [Fact]
    public async Task Logout_EndsThatSession_AndIsNotTreatedAsTheft()
    {
        var (client, email) = await RegisteredAsync();
        var tokens = await LoginAsync(client, email);
        var other = await LoginAsync(client, email);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/v1/auth/revoke", new { refreshToken = tokens.RefreshToken })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, tokens.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, other.RefreshToken)).StatusCode);

        using var scope = factory.SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revoked = await db.RefreshTokens.SingleAsync(r => r.TokenHash == TokenService.Hash(tokens.RefreshToken!));
        Assert.Equal(RefreshTokenRevocation.Logout, revoked.RevokedReason);
    }

    [Fact]
    public async Task Rotation_NeverExtendsTheSessionPastTheFamilyLifetime()
    {
        var (client, email) = await RegisteredAsync();
        var tokens = await LoginAsync(client, email);

        // Pretend the login was 29.5 days ago: the family ends in half a day.
        using (var scope = factory.SystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var started = DateTime.UtcNow.AddDays(-29.5);
            await db.RefreshTokens.Where(r => r.TokenHash == TokenService.Hash(tokens.RefreshToken!))
                .ExecuteUpdateAsync(r => r.SetProperty(x => x.FamilyStartedAt, started));
        }

        var rotated = await (await RefreshAsync(client, tokens.RefreshToken)).Content.ReadFromJsonAsync<Tokens>();

        using var check = factory.SystemScope();
        var next = await check.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens
            .SingleAsync(r => r.TokenHash == TokenService.Hash(rotated!.RefreshToken!));
        Assert.InRange(next.ExpiresAt, DateTime.UtcNow.AddHours(11), DateTime.UtcNow.AddHours(13));
    }

    [Fact]
    public async Task CookieMode_KeepsTheRefreshTokenOutOfTheBody_InAStrictHttpOnlyCookie()
    {
        var (client, email) = await RegisteredAsync();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login?useCookies=true", new { email, password = "Test123!" });

        var body = await response.Content.ReadFromJsonAsync<Tokens>();
        Assert.Null(body!.RefreshToken);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{RefreshTokenCookie.Name}=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CookieMode_RefreshRotatesTheCookie_AndLogoutClearsIt()
    {
        var (client, email) = await RegisteredAsync(handleCookies: true);
        await LoginAsync(client, email, cookies: true);

        var refresh = await client.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Null((await refresh.Content.ReadFromJsonAsync<Tokens>())!.RefreshToken);
        Assert.Contains(refresh.Headers.GetValues("Set-Cookie"), c => c.StartsWith($"{RefreshTokenCookie.Name}="));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Refresh_WithNoTokenAnywhere_IsUnauthorized()
    {
        var (client, _) = await RegisteredAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }
}
