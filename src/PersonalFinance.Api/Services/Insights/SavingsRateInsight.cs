using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// (income − spending) ÷ income for the month, and for each of the 12
// months ending with it. Income is take-home pay, so this is a take-home
// savings rate. Negative when more was spent than earned; null with no
// income.
public class SavingsRateInsight(AppDbContext db) : IInsight<SavingsRateResponse>
{
    public static string Key => "savings-rate";

    public const int Months = 12;

    public async Task<SavingsRateResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var months = await MonthlyCashflow.ForAsync(db, month.AddMonths(-(Months - 1)), month, cancellationToken);
        var current = months[^1];

        return new SavingsRateResponse
        {
            Month = month.ToString(),
            Income = current.Income,
            Spending = current.Spending,
            SavingsRate = Ratio.Of(current.Net, current.Income),
            Months = months
                .Select(m => new SavingsRateMonth { Month = m.Month.ToString(), SavingsRate = Ratio.Of(m.Net, m.Income) })
                .ToList()
        };
    }
}
