using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Tests;

// TOTP MFA: setup, enable, login with a code or a recovery code, replay and
// brute-force protection, disable (#117).
public class MfaTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Test123!";
    private record Tokens(string Token, string? RefreshToken);

    private sealed record MfaUser(HttpClient Client, string Email, string Id, string Secret, List<string> RecoveryCodes);

    // An authenticator app, per RFC 6238: HMAC-SHA1 over the 30 s time step.
    // Offset lets a test use the next step's code (Identity accepts a few
    // steps either side), since the current one is spent by enabling.
    private static string Totp(string base32Secret, int stepOffset = 0)
    {
        var key = Base32Decode(base32Secret);
        var step = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + stepOffset;
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = input.Replace(" ", "").TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return bytes.ToArray();
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string? code = null, string? recoveryCode = null) =>
        factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = Password, twoFactorCode = code, twoFactorRecoveryCode = recoveryCode });

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/problem+json") return null;
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<MfaUser> UserWithMfaAsync()
    {
        var user = await TestUser.CreateAsync(factory);
        using var scope = factory.Services.CreateScope();
        var email = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == user.Id)).Email!;

        var setup = await (await user.Client.PostAsync("/api/v1/auth/mfa/setup", null)).Content.ReadFromJsonAsync<MfaSetupResponse>();
        var secret = setup!.SharedKey.Replace(" ", "").ToUpperInvariant();
        Assert.StartsWith("otpauth://totp/PersonalFinance:", setup.AuthenticatorUri);
        Assert.Contains($"secret={secret}", setup.AuthenticatorUri);

        var enable = await user.Client.PostAsJsonAsync("/api/v1/auth/mfa/enable", new { code = Totp(secret) });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var codes = (await enable.Content.ReadFromJsonAsync<MfaRecoveryCodesResponse>())!.RecoveryCodes;

        return new MfaUser(user.Client, email, user.Id, secret, codes);
    }

    [Fact]
    public async Task Enable_ReturnsTenRecoveryCodes_AndTurnsMfaOn()
    {
        var user = await UserWithMfaAsync();

        Assert.Equal(10, user.RecoveryCodes.Count);
        Assert.Equal(10, user.RecoveryCodes.Distinct().Count());
        var status = await user.Client.GetFromJsonAsync<MfaStatusResponse>("/api/v1/auth/mfa");
        Assert.True(status!.Enabled);
        Assert.Equal(10, status.RecoveryCodesLeft);
    }

    [Fact]
    public async Task Enable_WithAWrongCode_LeavesMfaOff()
    {
        var user = await TestUser.CreateAsync(factory);
        await user.Client.PostAsync("/api/v1/auth/mfa/setup", null);

        var enable = await user.Client.PostAsJsonAsync("/api/v1/auth/mfa/enable", new { code = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, enable.StatusCode);
        Assert.False((await user.Client.GetFromJsonAsync<MfaStatusResponse>("/api/v1/auth/mfa"))!.Enabled);
    }

    [Fact]
    public async Task Login_WithoutACode_AsksForOne_AndIssuesNoTokens()
    {
        var user = await UserWithMfaAsync();

        var response = await LoginAsync(user.Email);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(MfaProblemCodes.RequiresTwoFactor, await ProblemCodeAsync(response));
        Assert.DoesNotContain("token", (await response.Content.ReadAsStringAsync()).ToLowerInvariant().Replace("two-factor", ""));
    }

    [Fact]
    public async Task Login_WithAValidCode_Succeeds_ButTheSameCodeCantBeReplayed()
    {
        var user = await UserWithMfaAsync();
        var code = Totp(user.Secret, stepOffset: 1); // the current step's code was spent enabling

        var first = await LoginAsync(user.Email, code);
        var replay = await LoginAsync(user.Email, code);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull((await first.Content.ReadFromJsonAsync<Tokens>())!.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(MfaProblemCodes.InvalidTwoFactorCode, await ProblemCodeAsync(replay));
    }

    [Fact]
    public async Task RecoveryCode_WorksOnce()
    {
        var user = await UserWithMfaAsync();
        var recovery = user.RecoveryCodes[0];

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email, recoveryCode: recovery)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(user.Email, recoveryCode: recovery)).StatusCode);
        Assert.Equal(9, (await user.Client.GetFromJsonAsync<MfaStatusResponse>("/api/v1/auth/mfa"))!.RecoveryCodesLeft);
    }

    [Fact]
    public async Task WrongCodes_LockTheAccount_EvenForTheRightCode()
    {
        var user = await UserWithMfaAsync();

        for (var i = 0; i < 5; i++)
            await LoginAsync(user.Email, "123456");
        var withRightCode = await LoginAsync(user.Email, Totp(user.Secret, stepOffset: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, withRightCode.StatusCode);
        using var scope = factory.Services.CreateScope();
        var dbUser = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == user.Id);
        Assert.True(dbUser.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task WrongPasswords_LockTheAccountToo()
    {
        var user = await TestUser.CreateAsync(factory);
        using var scope = factory.Services.CreateScope();
        var email = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == user.Id)).Email!;
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wrong123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Disable_NeedsACode_AndThenLoginNeedsOnlyThePassword()
    {
        var user = await UserWithMfaAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await user.Client.PostAsJsonAsync("/api/v1/auth/mfa/disable", new { code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await user.Client.PostAsJsonAsync("/api/v1/auth/mfa/disable", new { code = user.RecoveryCodes[3] })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email)).StatusCode);
        Assert.False((await user.Client.GetFromJsonAsync<MfaStatusResponse>("/api/v1/auth/mfa"))!.Enabled);
    }

    [Fact]
    public async Task Setup_WhileEnabled_IsAConflict()
    {
        var user = await UserWithMfaAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await user.Client.PostAsync("/api/v1/auth/mfa/setup", null)).StatusCode);
    }

    [Fact]
    public async Task MfaEvents_AreAudited()
    {
        var user = await UserWithMfaAsync();
        await LoginAsync(user.Email, "123456");
        await LoginAsync(user.Email, recoveryCode: user.RecoveryCodes[1]);

        using var scope = factory.Services.CreateScope();
        var types = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents
            .Where(e => e.UserId == user.Id).Select(e => e.EventType).ToListAsync();
        Assert.Contains(AuditEventTypes.MfaEnabled, types);
        Assert.Contains(AuditEventTypes.MfaFailed, types);
        Assert.Contains(AuditEventTypes.MfaRecoveryCodeUsed, types);
    }

    [Fact]
    public async Task MfaEndpoints_RequireSignIn()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/mfa")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/mfa/setup", null)).StatusCode);
    }
}
