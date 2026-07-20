using Going.Plaid;
using Going.Plaid.Link;
using Going.Plaid.Entity;
using Going.Plaid.Item;
using Microsoft.AspNetCore.Identity;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Endpoints;

public static class PlaidEndpoints
{
    public static void MapPlaidEndpoints(this WebApplication app)
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

            return Results.Ok(new { link_token = response.LinkToken });
        }).RequireAuthorization();

        app.MapPost("/plaid/exchange-token", async (
            ExchangeTokenRequest request,
            PlaidClient plaid,
            AppDbContext db,
            UserManager<User> userManager,
            HttpContext http) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            var response = await plaid.ItemPublicTokenExchangeAsync(new ItemPublicTokenExchangeRequest
            {
                PublicToken = request.PublicToken
            });

            var account = new PersonalFinance.Api.Entities.Account
            {
                UserId = userId,
                PlaidAccessToken = response.AccessToken,
                BankName = "Unknown",
                AccountType = "Unknown",
                PlaidAccountId = response.ItemId
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            return Results.Ok("Bank account linked successfully");
        }).RequireAuthorization();
    }
}

public record ExchangeTokenRequest(string PublicToken);