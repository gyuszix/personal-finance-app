using System.Net.Http.Json;

namespace PersonalFinance.Tests;

public class CategoriesEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Categories_ListsEveryMonthsCategoriesOnce()
    {
        var user = await TestUser.CreateAsync(factory);
        var other = await TestUser.CreateAsync(factory);
        var account = await user.AddAccountAsync();
        await user.AddTransactionAsync(account, 10m, category: "TRAVEL");
        await user.AddTransactionAsync(account, 10m, category: "TRAVEL");
        await user.AddTransactionAsync(account, 10m, category: "FOOD_AND_DRINK", date: DateTime.UtcNow.AddMonths(-6));
        await user.AddTransactionAsync(account, 10m, category: null);
        await other.AddTransactionAsync(await other.AddAccountAsync(), 10m, category: "MEDICAL");

        var categories = await user.Client.GetFromJsonAsync<List<string>>("/api/v1/transactions/categories");

        Assert.Equal(["FOOD_AND_DRINK", "TRAVEL"], categories);
    }
}
