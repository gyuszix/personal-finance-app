using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// Months of cover = liquid cash today ÷ the burn rate. Liquid cash is
// checking, savings and similar (AccountClassification.IsLiquid). Balances
// are today's; the burn rate follows the selected month.
public class EmergencyFundInsight(AppDbContext db, TimeProvider clock) : IInsight<EmergencyFundResponse>
{
    public static string Key => "emergency-fund";

    public async Task<EmergencyFundResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var accounts = await db.Accounts
            .Select(a => new { a.AccountType, a.Subtype, a.Balance })
            .ToListAsync(cancellationToken);
        var liquid = accounts.Where(a => AccountClassification.IsLiquid(a.AccountType, a.Subtype)).ToList();
        var cash = liquid.Sum(a => a.Balance);

        var burn = await BurnRate.ForAsync(db, month, clock.GetUtcNow(), cancellationToken);

        return new EmergencyFundResponse
        {
            Month = month.ToString(),
            LiquidCash = cash,
            LiquidAccounts = liquid.Count,
            MonthlyBurnRate = burn.MonthlyAverage,
            MonthsCovered = burn.MonthlyAverage is { } average ? Ratio.Of(cash, average) : null
        };
    }
}
