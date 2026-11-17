using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;

namespace PersonalFinance.Tests;

// HTTPS-only with HSTS behind a TLS-terminating proxy, and the headers every
// response carries (#123).
public class HttpsHardeningTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private WebApplicationFactory<Program> BehindProxy() => factory.WithWebHostBuilder(b =>
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:Enabled"] = "true"
        })));

    // HSTS is never sent for localhost, so use a real-looking host.
    private static HttpClient Client(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://api.example.com"), AllowAutoRedirect = false });

    private static HttpRequestMessage Get(string path, string? forwardedProto = null, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (forwardedProto is not null) request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    [Fact]
    public async Task ProxiedHttpsRequests_GetHsts()
    {
        using var app = BehindProxy();
        var response = await Client(app).SendAsync(Get("/health/live", forwardedProto: "https"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hsts = Assert.Single(response.Headers.GetValues("Strict-Transport-Security"));
        Assert.Equal("max-age=31536000; includeSubDomains", hsts);
    }

    [Fact]
    public async Task PlainHttp_IsRedirectedToHttps_AndGetsNoHsts()
    {
        using var app = BehindProxy();
        var response = await Client(app).SendAsync(Get("/api/v1/transactions", forwardedProto: "http"));

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://api.example.com/api/v1/transactions", response.Headers.Location!.ToString());
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task HealthProbes_OverPlainHttp_AreNotRedirected()
    {
        using var app = BehindProxy();
        var response = await Client(app).SendAsync(Get("/health/ready"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EveryResponse_CarriesTheSecurityHeaders()
    {
        var response = await Client(factory).SendAsync(Get("/health/live"));

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task BehindTheProxy_TheRealClientIp_IsWhatGetsAudited()
    {
        using var app = BehindProxy();
        var client = Client(app);
        var email = $"{Guid.NewGuid():N}@example.com";
        var register = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        { Content = JsonContent.Create(new { email, password = "Test123!" }) };
        register.Headers.Add("X-Forwarded-Proto", "https");
        (await client.SendAsync(register)).EnsureSuccessStatusCode();
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        { Content = JsonContent.Create(new { email, password = "Test123!" }) };
        login.Headers.Add("X-Forwarded-Proto", "https");
        login.Headers.Add("X-Forwarded-For", "203.0.113.9");
        (await client.SendAsync(login)).EnsureSuccessStatusCode();

        using var scope = app.SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = (await db.Users.SingleAsync(u => u.Email == email)).Id;
        var audited = await db.AuditEvents.SingleAsync(e => e.UserId == userId && e.EventType == AuditEventTypes.LoginSucceeded);
        Assert.Equal("203.0.113.9", audited.IpAddress);
    }

    [Fact]
    public async Task WithoutTheProxySetting_ForwardedHeadersAreIgnored()
    {
        // Directly reachable: a client claiming to be "https" or someone else
        // must not be believed.
        var response = await Client(factory).SendAsync(Get("/health/live", forwardedProto: "https", forwardedFor: "198.51.100.1"));

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }
}
