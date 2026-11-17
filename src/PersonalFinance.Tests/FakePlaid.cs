using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace PersonalFinance.Tests;

// Stands in for Plaid's HTTP API behind the real Going.Plaid client, so
// tests exercise the SDK's (de)serialisation too. Routes by path; anything
// not configured is a 400 like Plaid's own errors.
public sealed class FakePlaid
{
    private readonly ConcurrentDictionary<string, Func<JsonObject, Task<object>>> _routes = new();
    public ConcurrentQueue<(string Path, JsonObject Body)> Requests { get; } = new();

    public void On(string path, Func<JsonObject, object> respond) => _routes[path] = body => Task.FromResult(respond(body));
    public void OnAsync(string path, Func<JsonObject, Task<object>> respond) => _routes[path] = respond;

    // /transactions/sync keyed by the cursor sent ("" for none).
    public void SyncPages(Dictionary<string, object> pagesByCursor) =>
        On("/transactions/sync", body => pagesByCursor[body["cursor"]?.GetValue<string>() ?? ""]);

    public void Balances(string itemId, params (string AccountId, decimal Current)[] accounts) =>
        On("/accounts/balance/get", _ => new
        {
            accounts = accounts.Select(a => new
            {
                account_id = a.AccountId, name = "Account", type = "depository", subtype = "checking",
                balances = new { current = a.Current, iso_currency_code = "USD" }
            }),
            item = new { item_id = itemId, institution_id = "ins_fake" },
            request_id = "req"
        });

    public static object Page(string nextCursor, object[]? added = null, object[]? modified = null, string[]? removed = null,
        bool hasMore = false) => new
    {
        added = added ?? [], modified = modified ?? [],
        removed = (removed ?? []).Select(id => new { transaction_id = id }),
        next_cursor = nextCursor, has_more = hasMore, request_id = "req", accounts = Array.Empty<object>()
    };

    public static object Txn(string id, string accountId, decimal amount, string date = "2026-11-01", bool pending = false,
        string category = "FOOD_AND_DRINK", string name = "Coffee") => new
    {
        transaction_id = id, account_id = accountId, amount, date, name, merchant_name = name, pending,
        iso_currency_code = "USD",
        personal_finance_category = new { primary = category, detailed = category + "_OTHER" }
    };

    internal async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null
            ? new JsonObject()
            : JsonNode.Parse(await request.Content.ReadAsStringAsync())?.AsObject() ?? new JsonObject();
        Requests.Enqueue((path, body));

        if (!_routes.TryGetValue(path, out var respond))
            return Json(HttpStatusCode.BadRequest, new
            {
                error_type = "INVALID_REQUEST", error_code = "NOT_FAKED", error_message = $"FakePlaid has no route for {path}",
                request_id = "req"
            });

        return Json(HttpStatusCode.OK, await respond(body));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object payload) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(FakePlaid plaid) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            plaid.HandleAsync(request);
    }

    // A new handler per pipeline (HttpClientFactory disposes them), all
    // answering from this one FakePlaid.
    internal void Install(IServiceCollection services) =>
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => new Handler(this)));
}

// A factory whose outgoing HTTP (i.e. Plaid) goes to a FakePlaid.
public class FakePlaidWebApplicationFactory : TestWebApplicationFactory
{
    public FakePlaid Plaid { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(Plaid.Install);
    }
}
