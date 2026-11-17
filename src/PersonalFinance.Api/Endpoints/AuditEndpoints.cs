using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.DTOs;
using PersonalFinance.Api.Entities;
using PersonalFinance.Api.Middleware;
using PersonalFinance.Shared.DTOs;

namespace PersonalFinance.Api.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        // The signed-in user's own security events, newest first (#116).
        app.MapGet("/audit", async (
            [AsParameters] PageParameters query,
            AppDbContext db,
            UserManager<User> userManager,
            HttpContext http) =>
        {
            var userId = userManager.GetUserId(http.User);
            if (userId == null) return Results.Unauthorized();

            var events = db.AuditEvents.Where(e => e.UserId == userId);
            var total = await events.CountAsync();
            var rows = await events
                .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
                .Skip((query.ResolvedPage - 1) * query.ResolvedPageSize)
                .Take(query.ResolvedPageSize)
                .ToListAsync();

            return Results.Ok(new PagedResult<AuditEventResponse>
            {
                Items = rows.Select(e => new AuditEventResponse
                {
                    Id = e.Id,
                    OccurredAt = e.OccurredAt,
                    EventType = e.EventType,
                    IpAddress = e.IpAddress,
                    UserAgent = e.UserAgent,
                    Details = e.Details is null ? null : JsonSerializer.Deserialize<JsonElement>(e.Details)
                }).ToList(),
                Page = query.ResolvedPage,
                PageSize = query.ResolvedPageSize,
                TotalCount = total,
                HasMore = query.ResolvedPage * query.ResolvedPageSize < total
            });
        })
        .AddEndpointFilter<ValidationFilter<PageParameters>>()
        .RequireAuthorization();
    }
}
