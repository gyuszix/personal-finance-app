using PersonalFinance.Api.Data;
using PersonalFinance.Shared.DTOs.Insights;

namespace PersonalFinance.Api.Services.Insights;

// Card payments ÷ credit spending for the month. 100% or more means the
// cards were paid off in full; under 100% means a balance is building.
public class CardPayoffInsight(AppDbContext db) : IInsight<CardPayoffResponse>
{
    public static string Key => "card-payoff";

    public async Task<CardPayoffResponse> CalculateAsync(InsightMonth month, CancellationToken cancellationToken)
    {
        var rows = await PaymentMethod.SpendingAsync(db, month.Start, month.End, cancellationToken);
        var creditSpending = rows.Where(r => r.AccountType == PaymentMethod.Credit).Sum(r => r.Amount);
        var payments = await PaymentMethod.CardPaymentsAsync(db, month.Start, month.End, cancellationToken);

        return new CardPayoffResponse
        {
            Month = month.ToString(),
            CreditSpending = creditSpending,
            CardPayments = payments,
            PayoffRatio = Ratio.Of(payments, creditSpending)
        };
    }
}
