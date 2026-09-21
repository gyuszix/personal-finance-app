using Going.Plaid;
using Going.Plaid.Link;
using Going.Plaid.Entity;
using Going.Plaid.Item;
using Going.Plaid.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Services;

namespace PersonalFinance.Api.Endpoints;

public static class PlaidEndpoints
{
    public static void MapPlaidEndpoints(this IEndpointRouteBuilder app)
    {
        // Returns a link_token — frontend uses this to open the Plaid Link UI
        app.MapGet("/plaid/link-token", async (
            PlaidClient plaid,
            UserManager<User> userManager,
            HttpContext http) =>
        {
            // Get the logged-in user's ID from the JWT claims
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            var request = new LinkTokenCreateRequest
            {
                User = new LinkTokenCreateRequestUser { ClientUserId = userId },
                ClientName = "Personal Finance App",
                Products = [Products.Transactions],
                CountryCodes = [CountryCode.Us],
                Language = Language.English
            };

            var response = await plaid.LinkTokenCreateAsync(request);

            return Results.Ok(new { linkToken = response.LinkToken });
        }).RequireAuthorization();

        app.MapPost("/plaid/exchange-token", async (
            ExchangeTokenRequest request,
            PlaidClient plaid,
            AppDbContext db,
            UserManager<User> userManager,
            PlaidTokenProtector protector,
            HttpContext http) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            var exchangeResponse = await plaid.ItemPublicTokenExchangeAsync(new ItemPublicTokenExchangeRequest
            {
                PublicToken = request.PublicToken
            });

            // Real access token - only used to call Plaid, never persisted as-is
            var accessToken = exchangeResponse.AccessToken;
            if (string.IsNullOrEmpty(accessToken))
                return Results.BadRequest("Plaid rejected the public token - it may be invalid or already exchanged.");

            var accountsResponse = await plaid.AccountsGetAsync(new AccountsGetRequest
            {
                AccessToken = accessToken
            });

            var protectedAccessToken = protector.Protect(accessToken);

            var plaidAccountIds = accountsResponse.Accounts.Select(a => a.AccountId).ToList();
            var existingAccounts = await db.Accounts
                .Where(a => plaidAccountIds.Contains(a.PlaidAccountId))
                .ToDictionaryAsync(a => a.PlaidAccountId);

            foreach (var plaidAccount in accountsResponse.Accounts)
            {
                if (existingAccounts.TryGetValue(plaidAccount.AccountId, out var account))
                {
                    account.PlaidAccessToken = protectedAccessToken;
                    account.Balance = plaidAccount.Balances.Current ?? 0;
                }
                else
                {
                    db.Accounts.Add(new PersonalFinance.Api.Entities.Account
                    {
                        UserId = userId,
                        PlaidAccessToken = protectedAccessToken,
                        PlaidAccountId = plaidAccount.AccountId,
                        BankName = accountsResponse.Item.InstitutionId ?? "Unknown",
                        AccountType = plaidAccount.Type.ToString(),
                        Balance = plaidAccount.Balances.Current ?? 0
                    });
                }
            }

            await db.SaveChangesAsync();

            return Results.Ok("Bank account linked successfully");
        }).RequireAuthorization();
    }
}

public record ExchangeTokenRequest(string PublicToken);