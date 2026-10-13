using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Tests;

// Budgets with suggested limits, and budget variance (#88).
public class BudgetTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static readonly DateTime ThisMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Task<HttpResponseMessage> PutAsync(TestUser user, string category, object body) =>
        user.Client.PutAsJsonAsync($"/api/v1/budgets/{category}", body);

    [Fact]
    public async Task Budgets_SuggestTheRecentMonthlyAverage_RoundedUpToTen()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 200m, category: "FOOD_AND_DRINK", date: ThisMonth.AddMonths(-1).AddDays(1));
        await user.AddTransactionAsync(account, 161m, category: "FOOD_AND_DRINK", date: ThisMonth.AddMonths(-2).AddDays(1));
        await user.AddTransactionAsync(account, 900m, category: "TRANSFER_OUT", date: ThisMonth.AddMonths(-1).AddDays(1));
        await user.AddTransactionAsync(account, -900m, category: "INCOME", date: ThisMonth.AddMonths(-1).AddDays(1));

        var budgets = await user.Client.GetFromJsonAsync<List<BudgetResponse>>("/api/v1/budgets");

        var food = Assert.Single(budgets!);
        Assert.Equal("FOOD_AND_DRINK", food.Category);
        Assert.True(food.IsSuggested);
        Assert.Equal(130m, food.SuggestedLimit);   // 361 / 3 = 120.33 -> 130
        Assert.Equal(130m, food.MonthlyLimit);
    }

    [Fact]
    public async Task SavedLimit_ReplacesTheSuggestion_UntilDeleted()
    {
        var user = await TestUser.CreateAsync(factory);

        (await PutAsync(user, "TRAVEL", new { monthlyLimit = 250.005m })).EnsureSuccessStatusCode();
        var saved = Assert.Single((await user.Client.GetFromJsonAsync<List<BudgetResponse>>("/api/v1/budgets"))!);
        Assert.False(saved.IsSuggested);
        Assert.Equal(250.01m, saved.MonthlyLimit);

        (await PutAsync(user, "TRAVEL", new { monthlyLimit = 300m })).EnsureSuccessStatusCode();
        Assert.Equal(300m, Assert.Single((await user.Client.GetFromJsonAsync<List<BudgetResponse>>("/api/v1/budgets"))!).MonthlyLimit);

        (await user.Client.DeleteAsync("/api/v1/budgets/TRAVEL")).EnsureSuccessStatusCode();
        Assert.Empty((await user.Client.GetFromJsonAsync<List<BudgetResponse>>("/api/v1/budgets"))!);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public async Task SetBudget_RejectsAnOutOfRangeLimit(decimal limit)
    {
        var user = await TestUser.CreateAsync(factory);

        var response = await PutAsync(user, "TRAVEL", new { monthlyLimit = limit });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetBudget_RejectsAMissingLimit()
    {
        var user = await TestUser.CreateAsync(factory);

        var response = await PutAsync(user, "TRAVEL", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("INCOME")]
    [InlineData("TRANSFER_OUT")]
    [InlineData("NOT_A_CATEGORY")]
    public async Task SetBudget_RejectsCategoriesThatArentSpending(string category)
    {
        var user = await TestUser.CreateAsync(factory);

        var put = await PutAsync(user, category, new { monthlyLimit = 100m });
        var delete = await user.Client.DeleteAsync($"/api/v1/budgets/{category}");

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
    }

    [Fact]
    public async Task Budgets_RequireSignIn()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/budgets")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync("/api/v1/budgets/TRAVEL", new { monthlyLimit = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/v1/budgets/TRAVEL")).StatusCode);
    }

    [Fact]
    public async Task Budgets_AreIsolatedPerUser()
    {
        var alice = await TestUser.CreateAsync(factory);
        var bob = await TestUser.CreateAsync(factory);
        (await PutAsync(alice, "TRAVEL", new { monthlyLimit = 500m })).EnsureSuccessStatusCode();

        // Bob sees nothing, and writing the same category makes his own row
        (await PutAsync(bob, "TRAVEL", new { monthlyLimit = 50m })).EnsureSuccessStatusCode();
        (await bob.Client.DeleteAsync("/api/v1/budgets/TRAVEL")).EnsureSuccessStatusCode();
        Assert.Empty((await bob.Client.GetFromJsonAsync<List<BudgetResponse>>("/api/v1/budgets"))!);

        var aliceBudget = Assert.Single((await alice.Client.GetFromJsonAsync<List<BudgetResponse>>("/api/v1/budgets"))!);
        Assert.Equal(500m, aliceBudget.MonthlyLimit);
    }

    [Fact]
    public async Task WithoutSignedInUser_BudgetFilterMatchesNothing()
    {
        var user = await TestUser.CreateAsync(factory);
        (await PutAsync(user, "TRAVEL", new { monthlyLimit = 500m })).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.Budgets.ToListAsync());
        Assert.Contains(await db.Budgets.IgnoreQueryFilters().ToListAsync(), b => b.UserId == user.Id);
    }

    [Fact]
    public async Task Variance_ComparesSpendingWithSavedOrSuggestedLimits_OverBudgetFirst()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        var lastMonth = ThisMonth.AddMonths(-1).AddDays(1);
        await user.AddTransactionAsync(account, 300m, category: "FOOD_AND_DRINK", date: lastMonth);   // suggestion 100
        (await PutAsync(user, "TRAVEL", new { monthlyLimit = 400m })).EnsureSuccessStatusCode();
        (await PutAsync(user, "MEDICAL", new { monthlyLimit = 0m })).EnsureSuccessStatusCode();
        await user.AddTransactionAsync(account, 50m, category: "FOOD_AND_DRINK");
        await user.AddTransactionAsync(account, 500m, category: "TRAVEL");
        await user.AddTransactionAsync(account, 20m, category: "ENTERTAINMENT");   // new this month, no history

        var variance = await user.Client.GetFromJsonAsync<BudgetVarianceResponse>("/api/v1/insights/budget-variance");

        Assert.Equal(["TRAVEL", "ENTERTAINMENT", "FOOD_AND_DRINK", "MEDICAL"], variance!.Categories.Select(c => c.Category));
        var travel = variance.Categories[0];
        Assert.Equal(-100m, travel.Remaining);
        Assert.Equal(1.25m, travel.Used);
        Assert.False(travel.IsSuggested);
        var food = variance.Categories[2];
        Assert.True(food.IsSuggested);
        Assert.Equal(100m, food.Limit);
        Assert.Equal(0.5m, food.Used);
        Assert.Null(variance.Categories[3].Used);   // zero limit, nothing spent
        Assert.Equal(2, variance.OverBudget);         // travel, and entertainment against a 0 suggestion
        Assert.Equal(500m, variance.TotalLimit);
        Assert.Equal(570m, variance.TotalSpent);
    }

    [Fact]
    public async Task Variance_IsRecalculatedAfterALimitChanges()
    {
        var user = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 100m, category: "TRAVEL");
        (await PutAsync(user, "TRAVEL", new { monthlyLimit = 50m })).EnsureSuccessStatusCode();
        await user.Client.GetFromJsonAsync<BudgetVarianceResponse>("/api/v1/insights/budget-variance");

        (await PutAsync(user, "TRAVEL", new { monthlyLimit = 200m })).EnsureSuccessStatusCode();

        var variance = await user.Client.GetFromJsonAsync<BudgetVarianceResponse>("/api/v1/insights/budget-variance");
        Assert.Equal(100m, Assert.Single(variance!.Categories).Remaining);
    }
}
