using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Services;
using static PersonalFinance.Tests.FakePlaid;

namespace PersonalFinance.Tests;

// Only webhooks Plaid really signed - ES256, current key, fresh, covering
// this exact body - are acted on (#118).
public class PlaidWebhookTests : IClassFixture<FakePlaidWebApplicationFactory>
{
    private readonly FakePlaidWebApplicationFactory _factory;
    private readonly ECDsa _plaidKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _kid = $"kid-{Guid.NewGuid():N}";

    public PlaidWebhookTests(FakePlaidWebApplicationFactory factory)
    {
        _factory = factory;
        PublishKey(_kid, _plaidKey);
    }

    // What /webhook_verification_key/get returns for a key Plaid signs with.
    private void PublishKey(string kid, ECDsa key, long? expiredAt = null)
    {
        var p = key.ExportParameters(false);
        _factory.Plaid.On("/webhook_verification_key/get", body =>
            body["key_id"]?.GetValue<string>() == kid
                ? new
                {
                    key = new
                    {
                        alg = "ES256", crv = "P-256", kid, kty = "EC", use = "sig",
                        x = Base64UrlEncoder.Encode(p.Q.X), y = Base64UrlEncoder.Encode(p.Q.Y),
                        created_at = 1_700_000_000, expired_at = expiredAt
                    },
                    request_id = "req"
                }
                : new Error("INVALID_WEBHOOK_VERIFICATION_KEY_ID"));
    }

    private static string Sign(string body, ECDsa key, string kid, DateTimeOffset? issuedAt = null, string? claimedBody = null)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(claimedBody ?? body))).ToLowerInvariant();
        var token = new JwtSecurityToken(
            new JwtHeader(new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = kid }, SecurityAlgorithms.EcdsaSha256)),
            new JwtPayload(new[]
            {
                new Claim("iat", (issuedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("request_body_sha256", hash)
            }));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string SyncUpdatesAvailable(string itemId) =>
        $$"""{"webhook_type":"TRANSACTIONS","webhook_code":"SYNC_UPDATES_AVAILABLE","item_id":"{{itemId}}","initial_update_complete":true,"historical_update_complete":true,"environment":"sandbox"}""";

    private Task<HttpResponseMessage> PostAsync(string body, string? verification)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/plaid/webhook")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (verification is not null) request.Headers.Add("Plaid-Verification", verification);
        return _factory.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task SignedSyncUpdatesWebhook_SyncsThatItem()
    {
        var user = await TestUser.CreateAsync(_factory);
        var itemId = $"item-{Guid.NewGuid():N}";
        var plaidAccountId = $"acc-{Guid.NewGuid():N}";
        var protector = _factory.Services.GetRequiredService<PlaidTokenProtector>();
        var account = await user.AddAccountAsync(accessToken: protector.Protect("access-sandbox-webhook"), plaidAccountId: plaidAccountId);
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Accounts.IgnoreQueryFilters()
                .Where(a => a.AccountId == account.AccountId)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.PlaidItemId, itemId));
        }
        _factory.Plaid.SyncPages(new() { [""] = Page("w1", added: [Txn($"wh-{itemId}", plaidAccountId, 9.99m)]) });
        _factory.Plaid.Balances(itemId, (plaidAccountId, 50m));

        var body = SyncUpdatesAvailable(itemId);
        var response = await PostAsync(body, Sign(body, _plaidKey, _kid));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Synced in the background: wait for it.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        bool stored;
        do
        {
            using var scope = _factory.Services.CreateScope();
            stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Transactions.IgnoreQueryFilters()
                .AnyAsync(t => t.PlaidTransactionId == $"wh-{itemId}");
            if (!stored) await Task.Delay(100);
        } while (!stored && DateTime.UtcNow < deadline);
        Assert.True(stored, "webhook didn't trigger a sync of its Item");
    }

    [Fact]
    public async Task VerifiedButUnhandledWebhook_IsAcknowledged()
    {
        var body = """{"webhook_type":"ITEM","webhook_code":"WEBHOOK_UPDATE_ACKNOWLEDGED","item_id":"item-x"}""";
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(body, Sign(body, _plaidKey, _kid))).StatusCode);
    }

    [Fact]
    public async Task NoHeader_IsRejected() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(SyncUpdatesAvailable("item-x"), null)).StatusCode);

    [Fact]
    public async Task SignedByAnotherKey_IsRejected()
    {
        var body = SyncUpdatesAvailable("item-x");
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(body, Sign(body, attacker, _kid))).StatusCode);
    }

    [Fact]
    public async Task TamperedBody_IsRejected()
    {
        var signedFor = SyncUpdatesAvailable("item-legit");
        var sent = SyncUpdatesAvailable("item-attacker");
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(sent, Sign(sent, _plaidKey, _kid, claimedBody: signedFor))).StatusCode);
    }

    [Fact]
    public async Task OldWebhook_IsRejected_AsAReplay()
    {
        var body = SyncUpdatesAvailable("item-x");
        var sixMinutesAgo = DateTimeOffset.UtcNow.AddMinutes(-6);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(body, Sign(body, _plaidKey, _kid, sixMinutesAgo))).StatusCode);
    }

    [Fact]
    public async Task Hs256_IsRejected_EvenWithAValidLookingKid()
    {
        var body = SyncUpdatesAvailable("item-x");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var symmetric = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = _kid };
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            new JwtHeader(new SigningCredentials(symmetric, SecurityAlgorithms.HmacSha256)),
            new JwtPayload(new[]
            {
                new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim("request_body_sha256", hash)
            })));

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(body, token)).StatusCode);
    }

    [Fact]
    public async Task UnsignedAlgNone_IsRejected()
    {
        var body = SyncUpdatesAvailable("item-x");
        static string B64(string s) => Base64UrlEncoder.Encode(s);
        var token = $"{B64($$"""{"alg":"none","kid":"x"}""")}.{B64($$"""{"iat":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}""")}.";
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(body, token)).StatusCode);
    }

    [Fact]
    public async Task UnknownKid_IsRejected()
    {
        var body = SyncUpdatesAvailable("item-x");
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(body, Sign(body, _plaidKey, "kid-plaid-never-issued"))).StatusCode);
    }

    [Fact]
    public async Task ExpiredKey_IsRejected()
    {
        var kid = $"kid-expired-{Guid.NewGuid():N}";
        using var retired = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PublishKey(kid, retired, expiredAt: DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds());
        var body = SyncUpdatesAvailable("item-x");

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(body, Sign(body, retired, kid))).StatusCode);
    }

    [Fact]
    public async Task OversizedBody_IsRefused()
    {
        var body = $$"""{"webhook_type":"TRANSACTIONS","padding":"{{new string('x', 70_000)}}"}""";
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await PostAsync(body, Sign(body, _plaidKey, _kid))).StatusCode);
    }

    [Fact]
    public async Task RejectedWebhooks_AreAudited()
    {
        await PostAsync(SyncUpdatesAvailable("item-x"), "not-a-jwt");

        using var scope = _factory.Services.CreateScope();
        var rejected = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents
            .Where(e => e.EventType == AuditEventTypes.PlaidWebhookRejected)
            .OrderByDescending(e => e.Id).FirstAsync();
        Assert.Null(rejected.UserId);
        Assert.Contains("Malformed", rejected.Details);
    }
}
