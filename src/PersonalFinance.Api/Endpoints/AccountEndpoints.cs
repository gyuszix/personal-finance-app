using System.Security.Cryptography;
using Going.Plaid;
using Going.Plaid.Item;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Services;
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
            SummaryCache summaryCache,
            HttpContext http,
            ILogger<Program> logger) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            var cacheKey = summaryCache.AccountsSummaryKey(userId);
            if (summaryCache.TryGet<AccountsSummaryResponse>(cacheKey, out var cached))
            {
                logger.LogInformation("Returned accounts summary for user {UserId} from cache", userId);
                return Results.Ok(cached);
            }

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

            var response = new AccountsSummaryResponse
            {
                TotalBalance = accountResponses.Sum(a => a.Balance),
                TotalAssets = totalAssets,
                TotalLiabilities = totalLiabilities,
                NetWorth = totalAssets - totalLiabilities,
                Accounts = accountResponses
            };

            summaryCache.Set(cacheKey, response);
            return Results.Ok(response);
        }).RequireAuthorization();

        // Unlinks the bank behind this account. A Plaid Item (one bank
        // connection) backs every account it returned at link time, and
        // /item/remove can only remove the whole Item - so this removes all
        // of those accounts and their transactions, not just the one asked
        // for (#64).
        app.MapDelete("/accounts/{id}", async (
            int id,
            AppDbContext db,
            PlaidClient plaid,
            PlaidTokenProtector protector,
            SummaryCache summaryCache,
            ILogger<Program> logger) =>
        {
            // The per-user query filter makes someone else's account a 404.
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.AccountId == id);
            if (account == null) return Results.NotFound();

            var itemAccounts = await db.Accounts
                .Where(a => a.PlaidAccessToken == account.PlaidAccessToken)
                .ToListAsync();
            var itemAccountIds = itemAccounts.Select(a => a.AccountId).ToList();

            string? accessToken = null;
            try
            {
                accessToken = protector.Unprotect(account.PlaidAccessToken);
            }
            catch (CryptographicException)
            {
                // The key that encrypted it is gone, so Plaid can never be told
                // either - removing our copy is all that's left to do.
                logger.LogWarning(
                    "Unlinking accounts {AccountIds} without calling Plaid: access token no longer decrypts",
                    itemAccountIds);
            }

            if (accessToken != null)
            {
                var response = await plaid.ItemRemoveAsync(new ItemRemoveRequest { AccessToken = accessToken });

                // Already gone on Plaid's side is fine; anything else, keep our
                // data so the user can retry rather than orphaning a live Item.
                var alreadyGone = response.Error?.ErrorCode is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN";
                if (response.Error != null && !alreadyGone)
                {
                    logger.LogError(
                        "Plaid /item/remove failed for accounts {AccountIds}: {ErrorCode} {ErrorMessage}",
                        itemAccountIds, response.Error.ErrorCode, response.Error.ErrorMessage);
                    return Results.Problem(
                        title: "Couldn't disconnect the bank from Plaid. Try again later.",
                        statusCode: StatusCodes.Status502BadGateway);
                }
            }

            db.Transactions.RemoveRange(db.Transactions.Where(t => itemAccountIds.Contains(t.AccountId)));
            db.Accounts.RemoveRange(itemAccounts);
            await db.SaveChangesAsync();

            summaryCache.InvalidateForUser(account.UserId);

            logger.LogInformation("Unlinked Plaid Item: removed accounts {AccountIds}", itemAccountIds);
            return Results.NoContent();
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
