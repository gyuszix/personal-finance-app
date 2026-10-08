using System.Net;
using System.Net.Http.Json;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Tests;

public class TransactionEndpointsTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Delete_InvalidatesCachedSummary()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 10m);
        var doomed = await user.AddTransactionAsync(account, 25m);

        // Prime the cache.
        var before = await user.Client.GetFromJsonAsync<List<TransactionSummaryResponse>>("/api/v1/transactions/summary");
        Assert.Equal(35m, before!.Single().Total);

        var delete = await user.Client.DeleteAsync($"/api/v1/transactions/{doomed.TransactionId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var after = await user.Client.GetFromJsonAsync<List<TransactionSummaryResponse>>("/api/v1/transactions/summary");
        Assert.Equal(10m, after!.Single().Total);
    }

    [Fact]
    public async Task List_PagesNewestFirst()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
            await user.AddTransactionAsync(account, i + 1, date: now.AddDays(-i));

        var page1 = await user.Client.GetFromJsonAsync<PagedResult<TransactionResponse>>("/api/v1/transactions?page=1&pageSize=2");
        var page3 = await user.Client.GetFromJsonAsync<PagedResult<TransactionResponse>>("/api/v1/transactions?page=3&pageSize=2");

        Assert.Equal(5, page1!.TotalCount);
        Assert.Equal([1m, 2m], page1.Items.Select(t => t.Amount));
        Assert.True(page1.HasMore);
        Assert.Equal([5m], page3!.Items.Select(t => t.Amount));
        Assert.False(page3.HasMore);
    }

    [Fact]
    public async Task List_FiltersByCategory()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 10m, category: "FOOD_AND_DRINK");
        await user.AddTransactionAsync(account, 20m, category: "TRAVEL");

        var result = await user.Client.GetFromJsonAsync<PagedResult<TransactionResponse>>("/api/v1/transactions?category=TRAVEL");

        Assert.Equal([20m], result!.Items.Select(t => t.Amount));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=201")]
    public async Task List_RejectsOutOfRangePaging(string query)
    {
        var user = await TestUser.CreateAsync(factory);

        var response = await user.Client.GetAsync($"/api/v1/transactions?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Summary_GroupsByCategory_ExcludingPendingAndOtherMonths()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 10m, category: "FOOD_AND_DRINK");
        await user.AddTransactionAsync(account, 5m, category: "FOOD_AND_DRINK");
        await user.AddTransactionAsync(account, 40m, category: null);
        await user.AddTransactionAsync(account, 99m, category: "FOOD_AND_DRINK", pending: true);
        await user.AddTransactionAsync(account, 77m, category: "FOOD_AND_DRINK", date: DateTime.UtcNow.AddMonths(-2));

        var summary = await user.Client.GetFromJsonAsync<List<TransactionSummaryResponse>>("/api/v1/transactions/summary");

        Assert.Collection(summary!,
            s => { Assert.Equal("Uncategorized", s.Category); Assert.Equal(40m, s.Total); },
            s => { Assert.Equal("FOOD_AND_DRINK", s.Category); Assert.Equal(15m, s.Total); Assert.Equal(2, s.TransactionCount); });
    }

    [Fact]
    public async Task Summary_ForAGivenMonth_OnlyCountsThatMonth()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var march = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        await user.AddTransactionAsync(account, 12m, date: march);
        await user.AddTransactionAsync(account, 30m);

        var summary = await user.Client.GetFromJsonAsync<List<TransactionSummaryResponse>>("/api/v1/transactions/summary?month=2026-03");

        Assert.Equal(12m, summary!.Single().Total);
    }

    [Theory]
    [InlineData("/api/v1/transactions/summary?month=2026-13")]
    [InlineData("/api/v1/transactions/summary?month=March")]
    [InlineData("/api/v1/transactions/cashflow?month=2026/03")]
    public async Task MonthEndpoints_RejectMalformedMonth(string url)
    {
        var user = await TestUser.CreateAsync(factory);

        var response = await user.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cashflow_SplitsIncomeAndExpenses_IgnoringTransfers()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, -1000m, category: "INCOME");   // Plaid: negative = money in
        await user.AddTransactionAsync(account, 300m, category: "RENT_AND_UTILITIES");
        await user.AddTransactionAsync(account, 500m, category: "TRANSFER_OUT");
        await user.AddTransactionAsync(account, -500m, category: "TRANSFER_IN");

        var cashflow = await user.Client.GetFromJsonAsync<CashflowResponse>("/api/v1/transactions/cashflow");

        Assert.Equal(1000m, cashflow!.Income);
        Assert.Equal(300m, cashflow.Expenses);
        Assert.Equal(700m, cashflow.Net);
    }

    [Fact]
    public async Task Delete_SomeoneElsesTransaction_IsForbiddenAndKeepsIt()
    {
        var owner = await TestUser.CreateAsync(factory);
        var intruder = await TestUser.CreateAsync(factory);
        var transaction = await owner.AddTransactionAsync(await owner.AddAccountAsync(), 10m);

        var response = await intruder.Client.DeleteAsync($"/api/v1/transactions/{transaction.TransactionId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var stillThere = await owner.Client.GetFromJsonAsync<PagedResult<TransactionResponse>>("/api/v1/transactions");
        Assert.Single(stillThere!.Items);
    }

    [Fact]
    public async Task Delete_MissingTransaction_IsNotFound()
    {
        var user = await TestUser.CreateAsync(factory);

        var response = await user.Client.DeleteAsync("/api/v1/transactions/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/accounts/summary")).StatusCode);
    }
}
