using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Tests;

// Security events land in AuditEvents, are visible to their own user only,
// never carry secrets, and can't be edited after the fact (#116).
public class AuditLogTests(FakePlaidWebApplicationFactory factory) : IClassFixture<FakePlaidWebApplicationFactory>
{
    private record Tokens(string Token, string RefreshToken);

    private async Task<List<AuditEvent>> EventsForAsync(string? userId)
    {
        using var scope = factory.SystemScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents
            .Where(e => e.UserId == userId).OrderBy(e => e.Id).ToListAsync();
    }

    [Fact]
    public async Task Login_IsRecordedWithClientDetails_AndShownToTheUser()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PersonalFinanceTests/1.0");
        var email = $"{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "Test123!" });
        var tokens = (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Test123!" }))
            .Content.ReadFromJsonAsync<Tokens>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.Token);

        var page = await client.GetFromJsonAsync<PagedResult<AuditEventResponse>>("/api/v1/audit");

        // (TestServer requests carry no remote IP, so IpAddress isn't checked here.)
        var login = Assert.Single(page!.Items, e => e.EventType == AuditEventTypes.LoginSucceeded);
        Assert.Equal("PersonalFinanceTests/1.0", login.UserAgent);
        Assert.True(login.Details!.Value.TryGetProperty("sessionId", out _));
    }

    [Fact]
    public async Task FailedLogins_AreRecorded_WithoutTheAttemptedEmailOrPassword()
    {
        var client = factory.CreateClient();
        var unknownEmail = $"{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync("/api/v1/auth/login", new { email = unknownEmail, password = "Guess123!" });

        using var scope = factory.SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var anonymous = await db.AuditEvents
            .Where(e => e.UserId == null && e.EventType == AuditEventTypes.LoginFailed)
            .OrderByDescending(e => e.Id).FirstAsync();
        Assert.Contains("unknown-email", anonymous.Details);
        Assert.DoesNotContain(unknownEmail, anonymous.Details);
        Assert.DoesNotContain("Guess123!", anonymous.Details);
    }

    [Fact]
    public async Task WrongPassword_IsRecordedAgainstTheUser()
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "Test123!" });

        await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wrong123!" });

        using var scope = factory.SystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = (await db.Users.SingleAsync(u => u.Email == email)).Id;
        var failed = Assert.Single(await EventsForAsync(userId));
        Assert.Equal(AuditEventTypes.LoginFailed, failed.EventType);
        Assert.Contains("wrong-password", failed.Details);
    }

    [Fact]
    public async Task RefreshTokenReuse_AndLogout_AreRecorded()
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "Test123!" });
        var first = (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Test123!" }))
            .Content.ReadFromJsonAsync<Tokens>())!;
        var second = (await (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Test123!" }))
            .Content.ReadFromJsonAsync<Tokens>())!;

        await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken }); // reuse
        await client.PostAsJsonAsync("/api/v1/auth/revoke", new { refreshToken = second.RefreshToken });

        using var scope = factory.SystemScope();
        var userId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Email == email)).Id;
        var events = await EventsForAsync(userId);
        Assert.Equal(
            [AuditEventTypes.LoginSucceeded, AuditEventTypes.LoginSucceeded, AuditEventTypes.RefreshTokenReuseDetected, AuditEventTypes.Logout],
            events.Select(e => e.EventType));
        Assert.All(events, e =>
        {
            Assert.DoesNotContain(first.RefreshToken, e.Details ?? "");
            Assert.DoesNotContain(second.RefreshToken, e.Details ?? "");
        });
    }

    [Fact]
    public async Task LinkingABank_IsRecorded_WithoutThePlaidAccessToken()
    {
        var user = await TestUser.CreateAsync(factory);
        factory.Plaid.On("/item/public_token/exchange", _ => new
        {
            access_token = "access-sandbox-secret-value", item_id = "item-audit", request_id = "req"
        });
        factory.Plaid.On("/accounts/get", _ => new
        {
            accounts = new[]
            {
                new { account_id = $"acc-{Guid.NewGuid():N}", name = "Checking", type = "depository", subtype = "checking",
                      balances = new { current = 10m, iso_currency_code = "USD" } }
            },
            item = new { item_id = "item-audit", institution_id = "ins_audit" },
            request_id = "req"
        });
        factory.Plaid.On("/institutions/get_by_id", _ => new
        {
            institution = new { institution_id = "ins_audit", name = "Audit Bank", products = Array.Empty<string>(),
                                country_codes = new[] { "US" }, routing_numbers = Array.Empty<string>() },
            request_id = "req"
        });

        var response = await user.Client.PostAsJsonAsync("/api/v1/plaid/exchange-token", new { publicToken = "public-sandbox-x" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var linked = Assert.Single(await EventsForAsync(user.Id), e => e.EventType == AuditEventTypes.PlaidItemLinked);
        Assert.Contains("item-audit", linked.Details);
        Assert.DoesNotContain("access-sandbox-secret-value", linked.Details);
    }

    [Fact]
    public async Task UnlinkingABank_IsRecorded()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync(accessToken: "undecryptable");

        (await user.Client.DeleteAsync($"/api/v1/accounts/{account.AccountId}")).EnsureSuccessStatusCode();

        var unlinked = Assert.Single(await EventsForAsync(user.Id), e => e.EventType == AuditEventTypes.PlaidItemUnlinked);
        Assert.Contains(account.AccountId.ToString(), unlinked.Details);
        using var details = System.Text.Json.JsonDocument.Parse(unlinked.Details!);
        Assert.False(details.RootElement.GetProperty("plaidNotified").GetBoolean());
    }

    [Fact]
    public async Task Users_OnlySeeTheirOwnEvents()
    {
        var alice = await TestUser.CreateAsync(factory);
        var bob = await TestUser.CreateAsync(factory);

        var page = await bob.Client.GetFromJsonAsync<PagedResult<AuditEventResponse>>("/api/v1/audit");

        var aliceEventIds = (await EventsForAsync(alice.Id)).Select(e => e.Id).ToList();
        Assert.NotEmpty(aliceEventIds);
        Assert.Equal((await EventsForAsync(bob.Id)).Select(e => e.Id).Order(), page!.Items.Select(e => e.Id).Order());
        Assert.DoesNotContain(page.Items, e => aliceEventIds.Contains(e.Id));
    }

    [Fact]
    public async Task Audit_RequiresSignIn() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/audit")).StatusCode);

    [Theory]
    [InlineData("UPDATE \"AuditEvents\" SET \"EventType\" = 'tampered'")]
    [InlineData("DELETE FROM \"AuditEvents\"")]
    [InlineData("TRUNCATE \"AuditEvents\"")]
    public async Task AuditEvents_CannotBeChangedOrRemoved(string sql)
    {
        await TestUser.CreateAsync(factory); // at least one row

        // Layer 1: the API's role has no UPDATE/DELETE/TRUNCATE on the table (#119).
        using (var scope = factory.SystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var denied = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }

        // Layer 2: even the table's owner is stopped by the trigger (#116).
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("append-only", error.MessageText);
    }
}
