using PersonalFinance.Api.Entities;

namespace PersonalFinance.Api.Services;

// The one definition of which transactions are real income and spending,
// shared by cash flow and every insight built on it.
public static class Spending
{
    public const string CreditCardPayment = "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT";

    // Posted transactions that move money in or out of the user's finances.
    // Left out:
    // - pending: amount and category can still change before they post.
    // - TRANSFER_IN/TRANSFER_OUT: money moving between the user's own accounts.
    // - credit card payments: the purchases they pay for are already counted
    //   on the card, so counting the payment from checking too would count
    //   that spending twice - and its credit on the card side isn't income.
    public static IQueryable<Transaction> CountedInCashflow(this IQueryable<Transaction> transactions) =>
        transactions.Where(t =>
            !t.IsPending
            && t.CategoryPrimary != "TRANSFER_IN"
            && t.CategoryPrimary != "TRANSFER_OUT"
            && (t.CategoryDetailed == null || t.CategoryDetailed != CreditCardPayment));
}
