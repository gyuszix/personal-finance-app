using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PersonalFinance.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "super-secret-test-key-that-is-long-enough-32chars",
                ["Jwt:Issuer"] = "PersonalFinance.Api",
                ["Jwt:Audience"] = "PersonalFinance.Api"
            });
        });
    }
}

public class AuthEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_ReturnsOk()
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new
        {
            email = "test@example.com",
            password = "Test123!"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokens()
    {
        await _client.PostAsJsonAsync("/auth/register", new
        {
            email = "login@example.com",
            password = "Test123!"
        });

        var response = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = "login@example.com",
            password = "Test123!"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(body?.Token);
        Assert.NotNull(body?.RefreshToken);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        await _client.PostAsJsonAsync("/auth/register", new
        {
            email = "wrong@example.com",
            password = "Test123!"
        });

        var response = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = "wrong@example.com",
            password = "WrongPassword!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewTokens()
    {
        await _client.PostAsJsonAsync("/auth/register", new
        {
            email = "refresh@example.com",
            password = "Test123!"
        });

        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = "refresh@example.com",
            password = "Test123!"
        });

        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();

        var refreshResponse = await _client.PostAsJsonAsync("/auth/refresh", new
        {
            refreshToken = tokens!.RefreshToken
        });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var newTokens = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(newTokens?.Token);
        Assert.NotEqual(tokens.RefreshToken, newTokens?.RefreshToken);
    }

    [Fact]
    public async Task Refresh_WithUsedToken_ReturnsUnauthorized()
    {
        await _client.PostAsJsonAsync("/auth/register", new
        {
            email = "reuse@example.com",
            password = "Test123!"
        });

        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = "reuse@example.com",
            password = "Test123!"
        });

        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();

        await _client.PostAsJsonAsync("/auth/refresh", new
        {
            refreshToken = tokens!.RefreshToken
        });

        var reuseResponse = await _client.PostAsJsonAsync("/auth/refresh", new
        {
            refreshToken = tokens.RefreshToken
        });

        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    private record TokenResponse(string Token, string RefreshToken);
}