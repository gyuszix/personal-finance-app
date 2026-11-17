using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Observability;
using PersonalFinance.Api.Services;
using static PersonalFinance.Tests.FakePlaid;

namespace PersonalFinance.Tests;

// Traces carry what's needed to debug a sync or webhook, and nothing secret (#121).
public sealed class TelemetryTests : IClassFixture<FakePlaidWebApplicationFactory>, IDisposable
{
    private readonly FakePlaidWebApplicationFactory _factory;
    private readonly ActivityListener _listener;
    private readonly ConcurrentBag<Activity> _stopped = [];

    public TelemetryTests(FakePlaidWebApplicationFactory factory)
    {
        _factory = factory;
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name is Telemetry.Name or "Microsoft.AspNetCore" or "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Add
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task ItemSync_HasASpan_WithItsCounts()
    {
        var user = await TestUser.CreateAsync(_factory);
        var plaidId = $"acc-{Guid.NewGuid():N}";
        var protector = _factory.Services.GetRequiredService<PlaidTokenProtector>();
        var account = await user.AddAccountAsync(accessToken: protector.Protect("access-sandbox-otel", user.Id), plaidAccountId: plaidId);
        _factory.Plaid.SyncPages(new() { [""] = Page("c1", added: [Txn($"otel-{plaidId}", plaidId, 3m)]) });
        _factory.Plaid.Balances("item-otel-balances", (plaidId, 1m));

        // Unique, because the listener also sees other test classes' syncs.
        var itemId = $"item-otel-{Guid.NewGuid():N}";
        using (var scope = _factory.SystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Accounts.IgnoreQueryFilters().Where(a => a.AccountId == account.AccountId)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.PlaidItemId, itemId));
            var accounts = await db.Accounts.IgnoreQueryFilters().Where(a => a.AccountId == account.AccountId).ToListAsync();
            await scope.ServiceProvider.GetRequiredService<PlaidSyncService>().SyncItemAsync(accounts);
        }

        var span = Assert.Single(_stopped, a => a.OperationName == "plaid.sync_item"
                                                && Equals(a.GetTagItem("plaid.item.id"), itemId));
        Assert.Equal(1, span.GetTagItem("plaid.sync.added"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        // Its database work nests under it. (Plaid's HTTP spans would too,
        // but the fake Plaid replaces the handler that creates them.)
        Assert.Contains(_stopped, a => a.Source.Name == "Npgsql" && a.ParentSpanId == span.SpanId);
    }

    [Fact]
    public async Task RequestSpans_NeverCarryTheQueryString()
    {
        var user = await TestUser.CreateAsync(_factory);
        var marker = $"secret{Guid.NewGuid():N}";

        await user.Client.GetAsync($"/api/v1/transactions?category={marker}");
        // SignalR puts the access token in the query string.
        await _factory.CreateClient().PostAsync($"/hubs/transactions/negotiate?access_token={marker}", null);

        var serverSpans = _stopped.Where(a => a.Source.Name == "Microsoft.AspNetCore").ToList();
        Assert.NotEmpty(serverSpans);
        Assert.DoesNotContain(serverSpans.SelectMany(a => a.TagObjects), t => t.Value?.ToString()?.Contains(marker) == true);
        Assert.DoesNotContain(serverSpans, a => a.DisplayName.Contains(marker));
    }
}
