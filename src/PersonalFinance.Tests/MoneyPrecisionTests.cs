using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Tests;

// Money must be decimal with an explicit precision end to end: entity,
// column, DTO, and the Plaid SDK type we read it from (#112).
public class MoneyPrecisionTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private static readonly Type[] FloatingPoint = [typeof(float), typeof(double), typeof(float?), typeof(double?)];

    [Fact]
    public void NoEntity_HasFloatingPointProperties()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var offenders = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => (Entity: e.ClrType.Name, Property: p)))
            .Where(x => FloatingPoint.Contains(x.Property.ClrType))
            .Select(x => $"{x.Entity}.{x.Property.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryDecimalColumn_HasExplicitPrecision_AndCentsScale()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var decimals = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => (Entity: e.ClrType.Name, Property: p)))
            .Where(x => x.Property.ClrType == typeof(decimal) || x.Property.ClrType == typeof(decimal?))
            .ToList();

        Assert.NotEmpty(decimals);
        Assert.All(decimals, x =>
        {
            Assert.True(x.Property.GetPrecision() is not null, $"{x.Entity}.{x.Property.Name} has no precision");
            Assert.Equal(2, x.Property.GetScale());
        });
    }

    [Fact]
    public async Task MoneyColumns_AreNumericWithPrecision_InThePostgresSchema()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var columns = await db.Database.SqlQueryRaw<MoneyColumn>("""
            SELECT table_name AS "Table", column_name AS "Column", data_type AS "Type",
                   numeric_precision AS "Precision", numeric_scale AS "Scale"
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND (table_name, column_name) IN (('Transactions','Amount'), ('Accounts','Balance'),
                                                ('BalanceSnapshots','Balance'), ('Budgets','MonthlyLimit'))
            """).ToListAsync();

        Assert.Equal(4, columns.Count);
        Assert.All(columns, c =>
        {
            Assert.Equal("numeric", c.Type);
            Assert.NotNull(c.Precision);
            Assert.Equal(2, c.Scale);
        });
    }

    [Fact]
    public void NoSharedDto_HasFloatingPointProperties()
    {
        var offenders = typeof(PagedResult<>).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (Type: t, Property: p)))
            .Where(x => FloatingPoint.Contains(x.Property.PropertyType))
            .Select(x => $"{x.Type.Name}.{x.Property.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    // Plaid sends amounts as JSON numbers. If the SDK parsed them as double,
    // a value like 0.1 + 0.2 would be off before we ever saw it.
    [Fact]
    public void PlaidSdk_ParsesAmountsAsDecimal()
    {
        Assert.Equal(typeof(decimal?), typeof(Going.Plaid.Entity.Transaction).GetProperty("Amount")!.PropertyType);
        Assert.Equal(typeof(decimal?), typeof(Going.Plaid.Entity.AccountBalance).GetProperty("Current")!.PropertyType);
    }

    private record MoneyColumn(string Table, string Column, string Type, int? Precision, int? Scale);
}
