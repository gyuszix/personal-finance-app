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
using PersonalFinance.Api.Services.Webhooks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PersonalFinance.Api.Endpoints;

public static class PlaidEndpoints
{
    public static void MapPlaidEndpoints(this IEndpointRouteBuilder app)
    {
        // Returns a link_token — frontend uses this to open the Plaid Link UI
        app.MapGet("/plaid/link-token", async (
            PlaidClient plaid,
            UserManager<User> userManager,
            IConfiguration config,
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
                Language = Language.English,
                // Where Plaid sends webhooks for this Item (#118). Unset in
                // local dev: Plaid can't reach localhost, and the scheduled
                // and manual syncs still work without it.
                Webhook = string.IsNullOrWhiteSpace(config["Plaid:WebhookUrl"]) ? null : config["Plaid:WebhookUrl"]
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
            PlaidInstitutionService institutions,
            AuditLog audit,
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

            var protectedAccessToken = protector.Protect(accessToken, userId);

            var institutionId = accountsResponse.Item.InstitutionId;
            var bankName = await institutions.GetNameAsync(institutionId) ?? institutionId ?? "Unknown";

            var plaidAccountIds = accountsResponse.Accounts.Select(a => a.AccountId).ToList();
            var existingAccounts = await db.Accounts
                .Where(a => plaidAccountIds.Contains(a.PlaidAccountId))
                .ToDictionaryAsync(a => a.PlaidAccountId);

            foreach (var plaidAccount in accountsResponse.Accounts)
            {
                if (existingAccounts.TryGetValue(plaidAccount.AccountId, out var account))
                {
                    account.PlaidAccessToken = protectedAccessToken;
                    account.BankName = bankName;
                    account.Balance = plaidAccount.Balances.Current ?? 0;
                    account.Subtype = plaidAccount.Subtype?.ToString();
                    account.PlaidItemId = accountsResponse.Item.ItemId;
                }
                else
                {
                    db.Accounts.Add(new PersonalFinance.Api.Entities.Account
                    {
                        UserId = userId,
                        PlaidAccessToken = protectedAccessToken,
                        PlaidAccountId = plaidAccount.AccountId,
                        BankName = bankName,
                        AccountType = plaidAccount.Type.ToString(),
                        Subtype = plaidAccount.Subtype?.ToString(),
                        PlaidItemId = accountsResponse.Item.ItemId,
                        Balance = plaidAccount.Balances.Current ?? 0
                    });
                }
            }

            await db.SaveChangesAsync();

            await audit.RecordAsync(AuditEventTypes.PlaidItemLinked, userId, new
            {
                itemId = accountsResponse.Item.ItemId,
                institutionId,
                accountCount = accountsResponse.Accounts.Count
            });

            return Results.Ok("Bank account linked successfully");
        }).RequireAuthorization();

        // Plaid -> us. Anonymous by nature, so every request must prove it
        // came from Plaid via the signed Plaid-Verification header before
        // anything in the body is trusted (#118).
        app.MapPost("/plaid/webhook", async (
            HttpContext http,
            PlaidWebhookVerifier verifier,
            PlaidItemSyncQueue syncQueue,
            AuditLog audit,
            ILogger<PlaidWebhookVerifier> logger,
            CancellationToken cancellationToken) =>
        {
            var body = await ReadBodyAsync(http.Request, MaxWebhookBytes, cancellationToken);
            if (body is null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var result = await verifier.VerifyAsync(http.Request.Headers["Plaid-Verification"], body, cancellationToken);
            if (result != WebhookVerification.Valid)
            {
                await audit.RecordAsync(AuditEventTypes.PlaidWebhookRejected, null, new { reason = result.ToString() });
                return Results.Unauthorized();
            }

            PlaidWebhook? webhook;
            try
            {
                webhook = JsonSerializer.Deserialize<PlaidWebhook>(body);
            }
            catch (JsonException)
            {
                return Results.BadRequest();
            }
            if (webhook is null) return Results.BadRequest();

            logger.LogInformation("Plaid webhook {WebhookType}/{WebhookCode} for Item {ItemId}",
                webhook.WebhookType, webhook.WebhookCode, webhook.ItemId);

            // The only one that needs action today: new or changed
            // transactions are ready for /transactions/sync.
            if (webhook is { WebhookType: "TRANSACTIONS", WebhookCode: "SYNC_UPDATES_AVAILABLE", ItemId: { } itemId })
                syncQueue.Enqueue(itemId);

            // 200 for everything verified, handled or not, so Plaid doesn't retry.
            return Results.Ok();
        })
        .AllowAnonymous()
        .ExcludeFromDescription();
    }

    private const int MaxWebhookBytes = 64 * 1024;

    // The raw bytes, because the signature covers exactly what Plaid sent;
    // null if it's bigger than any real webhook.
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, int maxBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBytes) return null;
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private record PlaidWebhook(
        [property: JsonPropertyName("webhook_type")] string? WebhookType,
        [property: JsonPropertyName("webhook_code")] string? WebhookCode,
        [property: JsonPropertyName("item_id")] string? ItemId);
}

public record ExchangeTokenRequest(string PublicToken);
