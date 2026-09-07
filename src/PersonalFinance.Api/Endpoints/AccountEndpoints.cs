using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // Balances overview - total balance, assets/liabilities/net worth,
        // and a per-account breakdown. The top-line numbers for the ratios page.
        app.MapGet("/accounts/summary", async (
            AppDbContext db,
            UserManager<User> userManager,
            HttpContext http,
            ILogger<Program> logger) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            var accounts = await db.Accounts.ToListAsync();

            var accountResponses = accounts
                .Select(a => new AccountResponse
                {
                    AccountId = a.AccountId,
                    BankName = a.BankName,
                    AccountType = a.AccountType,
                    Balance = a.Balance,
                    Classification = ClassifyAccount(a.AccountType)
                })
                .ToList();

            var totalAssets = accountResponses
                .Where(a => a.Classification == "Asset")
                .Sum(a => a.Balance);

            var totalLiabilities = accountResponses
                .Where(a => a.Classification == "Liability")
                .Sum(a => a.Balance);

            logger.LogInformation(
                "Returned accounts summary for user {UserId}: {AccountCount} accounts, netWorth {NetWorth}",
                userId, accountResponses.Count, totalAssets - totalLiabilities);

            return Results.Ok(new AccountsSummaryResponse
            {
                TotalBalance = accountResponses.Sum(a => a.Balance),
                TotalAssets = totalAssets,
                TotalLiabilities = totalLiabilities,
                NetWorth = totalAssets - totalLiabilities,
                Accounts = accountResponses
            });
        }).RequireAuthorization();
    }

    // Plaid's own account type classification - depository/investment
    // accounts are assets, credit/loan accounts are liabilities (balance
    // represents amount owed, not negative equity - see NetWorth calc above).
    // Anything unrecognized is "Other" and deliberately excluded from both
    // totals rather than guessed at.
    private static string ClassifyAccount(string accountType) => accountType switch
    {
        "Depository" or "Investment" => "Asset",
        "Credit" or "Loan" => "Liability",
        _ => "Other"
    };
}
