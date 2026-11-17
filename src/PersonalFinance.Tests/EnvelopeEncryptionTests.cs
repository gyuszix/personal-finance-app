using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Auth;
using PersonalFinance.Api.Services.Encryption;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Tests;

// Envelope encryption of stored secrets, and rotating the key that wraps
// them (#120).
public class EnvelopeEncryptionTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static EnvelopeEncryptor Envelope(string current, params (string Id, string Key)[] keys)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            keys.Select(k => new KeyValuePair<string, string?>($"Encryption:Keys:{k.Id}", k.Key))
                .Append(new("Encryption:CurrentKeyId", current))).Build();
        return new EnvelopeEncryptor(new LocalKeyEncryptionKeyProvider(config));
    }

    // The same app, with k2 added as the current key and k1 kept to read old values.
    private WebApplicationFactory<Program> RotatedToK2(bool keepK1 = true) => factory.WithWebHostBuilder(b =>
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:CurrentKeyId"] = "test-k2",
            ["Encryption:Keys:test-k2"] = TestKeys.K2,
            ["Encryption:Keys:test-k1"] = keepK1 ? TestKeys.K1 : null
        })));

    [Fact]
    public void RoundTrips_WithAFreshDataKeyEveryTime()
    {
        var envelope = Envelope("k1", ("k1", TestKeys.K1));

        var a = envelope.Encrypt("access-sandbox-1", "ctx");
        var b = envelope.Encrypt("access-sandbox-1", "ctx");

        Assert.StartsWith("pfenc:v1:k1:", a);
        Assert.NotEqual(a, b);
        Assert.Equal("access-sandbox-1", envelope.Decrypt(a, "ctx"));
        Assert.DoesNotContain("access-sandbox", a);
    }

    [Fact]
    public void Tampering_OrTheWrongContext_FailsToDecrypt()
    {
        var envelope = Envelope("k1", ("k1", TestKeys.K1));
        var value = envelope.Encrypt("secret", "user-a");

        Assert.ThrowsAny<CryptographicException>(() => envelope.Decrypt(value, "user-b"));
        var flipped = value[..^2] + (value[^2] == 'A' ? 'B' : 'A') + value[^1];
        Assert.ThrowsAny<CryptographicException>(() => envelope.Decrypt(flipped, "user-a"));
        // Relabelling the wrapped key as another KEK's doesn't work either.
        var relabelled = value.Replace("pfenc:v1:k1:", "pfenc:v1:k2:");
        Assert.ThrowsAny<CryptographicException>(() => Envelope("k2", ("k1", TestKeys.K1), ("k2", TestKeys.K1)).Decrypt(relabelled, "user-a"));
    }

    [Fact]
    public void Rewrap_MovesTheDataKeyToTheCurrentKek_WithoutTouchingTheCiphertext()
    {
        var old = Envelope("k1", ("k1", TestKeys.K1)).Encrypt("secret", "ctx");
        var rotated = Envelope("k2", ("k1", TestKeys.K1), ("k2", TestKeys.K2));

        var rewrapped = rotated.Rewrap(old);

        Assert.StartsWith("pfenc:v1:k2:", rewrapped);
        Assert.Equal(old.Split(':')[^1], rewrapped.Split(':')[^1]); // same nonce|ciphertext|tag
        Assert.Equal("secret", Envelope("k2", ("k2", TestKeys.K2)).Decrypt(rewrapped, "ctx")); // k1 can now go
        Assert.Equal(rewrapped, rotated.Rewrap(rewrapped)); // idempotent
    }

    [Fact]
    public void MissingOrBadKeys_FailAtStartup_WithInstructions()
    {
        var none = Assert.Throws<InvalidOperationException>(() => Envelope("k1"));
        Assert.Contains("new-kek.sh", none.Message);
        Assert.Throws<InvalidOperationException>(() => Envelope("k1", ("k1", Convert.ToBase64String(new byte[16]))));
        Assert.Throws<InvalidOperationException>(() => Envelope("k9", ("k1", TestKeys.K1)));
    }

    [Fact]
    public async Task PlaidTokens_AreStoredAsEnvelopes_BoundToTheirUser()
    {
        var alice = await TestUser.CreateAsync(factory);
        var protector = factory.Services.GetRequiredService<PlaidTokenProtector>();

        var stored = protector.Protect("access-sandbox-alice", alice.Id);

        Assert.StartsWith("pfenc:v1:test-k1:", stored);
        Assert.Equal("access-sandbox-alice", protector.Unprotect(stored, alice.Id));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(stored, "someone-else"));
    }

    [Fact]
    public async Task TotpSecret_AndRecoveryCodes_AreEncryptedInTheDatabase()
    {
        var user = await TestUser.CreateAsync(factory);
        var setup = await (await user.Client.PostAsync("/api/v1/auth/mfa/setup", null)).Content.ReadFromJsonAsync<MfaSetupResponse>();

        using var scope = factory.SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.UserTokens.SingleAsync(t => t.UserId == user.Id && t.Name == "AuthenticatorKey");
        Assert.StartsWith("pfenc:v1:", stored.Value);
        Assert.DoesNotContain(setup!.SharedKey.Replace(" ", "").ToUpperInvariant(), stored.Value);
    }

    [Fact]
    public async Task RewrapSecrets_MovesEverythingToTheNewKey_AndUpgradesLegacyTokens()
    {
        var user = await TestUser.CreateAsync(factory);
        var protector = factory.Services.GetRequiredService<PlaidTokenProtector>();
        var legacy = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(PlaidTokenProtector.LegacyPurpose);
        // Two accounts of one Item share a ciphertext; a third is pre-#120.
        var shared = protector.Protect("access-sandbox-item", user.Id);
        var checking = await user.AddAccountAsync(accessToken: shared);
        var savings = await user.AddAccountAsync(accessToken: shared);
        var old = await user.AddAccountAsync(accessToken: legacy.Protect("access-sandbox-legacy"));
        await user.Client.PostAsync("/api/v1/auth/mfa/setup", null);

        using var rotated = RotatedToK2();
        using (var scope = rotated.Services.CreateScope())
        {
            var report = await scope.ServiceProvider.GetRequiredService<SecretRewrapper>().RewrapAllAsync();
            Assert.Equal(0, report.Failed);
            Assert.False(report.RemainingByKeyId.ContainsKey("test-k1"));
            Assert.False(report.RemainingByKeyId.ContainsKey("legacy"));
        }

        // k1 retired: everything still decrypts with k2 alone.
        using var k2Only = RotatedToK2(keepK1: false);
        using var check = k2Only.SystemScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var accounts = await db.Accounts.IgnoreQueryFilters()
            .Where(a => new[] { checking.AccountId, savings.AccountId, old.AccountId }.Contains(a.AccountId))
            .ToDictionaryAsync(a => a.AccountId);
        var k2Protector = k2Only.Services.GetRequiredService<PlaidTokenProtector>();
        Assert.All(accounts.Values, a => Assert.StartsWith("pfenc:v1:test-k2:", a.PlaidAccessToken));
        Assert.Equal(accounts[checking.AccountId].PlaidAccessToken, accounts[savings.AccountId].PlaidAccessToken); // still one Item
        Assert.Equal("access-sandbox-item", k2Protector.Unprotect(accounts[checking.AccountId].PlaidAccessToken, user.Id));
        Assert.Equal("access-sandbox-legacy", k2Protector.Unprotect(accounts[old.AccountId].PlaidAccessToken, user.Id));
        var totp = await db.UserTokens.SingleAsync(t => t.UserId == user.Id && t.Name == "AuthenticatorKey");
        Assert.StartsWith("pfenc:v1:test-k2:", totp.Value);
        var store = check.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PersonalFinance.Api.Entities.User>>();
        Assert.NotNull(await store.GetAuthenticatorKeyAsync((await store.FindByIdAsync(user.Id))!));
    }
}
