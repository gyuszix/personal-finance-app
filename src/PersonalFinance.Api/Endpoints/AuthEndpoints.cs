using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.DTOs;
using PersonalFinance.Api.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace PersonalFinance.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/auth/register", async (
            RegisterRequest request,
            UserManager<User> userManager) =>
        {
            var user = new User { UserName = request.Email, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);

            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, "User");
                return Results.Ok("User registered successfully");
            }

            return Results.BadRequest(result.Errors);
        });

        app.MapPost("/auth/login", async (
            LoginRequest request,
            UserManager<User> userManager,
            AppDbContext db,
            IConfiguration config) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
                return Results.Unauthorized();

            var jwt = GenerateJwt(user, await userManager.GetRolesAsync(user), config);
            var refreshToken = GenerateRefreshToken(user.Id);

            db.RefreshTokens.Add(refreshToken);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                token = jwt,
                refreshToken = refreshToken.Token
            });
        });

        // Issues a new JWT + refresh token, rotates the old refresh token
        app.MapPost("/auth/refresh", async (
            RefreshRequest request,
            AppDbContext db,
            UserManager<User> userManager,
            IConfiguration config) =>
        {
            var existing = await db.RefreshTokens
                .FirstOrDefaultAsync(r => r.Token == request.RefreshToken);

            // Token not found
            if (existing == null)
            {
                return Results.Unauthorized();
            }

            // Reuse detection — token was already used, nuke the whole family
            if (existing.IsRevoked)
            {
                var allUserTokens = db.RefreshTokens
                    .Where(r => r.UserId == existing.UserId);
                db.RefreshTokens.RemoveRange(allUserTokens);
                await db.SaveChangesAsync();
                return Results.Unauthorized();
            }

            // Token expired
            if (existing.ExpiresAt < DateTime.UtcNow)
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(existing.UserId);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            // Rotate — revoke old, issue new
            existing.IsRevoked = true;
            var newRefreshToken = GenerateRefreshToken(user.Id);
            existing.ReplacedByToken = newRefreshToken.Token;

            db.RefreshTokens.Add(newRefreshToken);
            await db.SaveChangesAsync();

            var jwt = GenerateJwt(user, await userManager.GetRolesAsync(user), config);

            return Results.Ok(new
            {
                token = jwt,
                refreshToken = newRefreshToken.Token
            });
        });

        // Revokes a refresh token — effectively logs the user out
        app.MapPost("/auth/revoke", async (
            RefreshRequest request,
            AppDbContext db) =>
        {
            var existing = await db.RefreshTokens
                .FirstOrDefaultAsync(r => r.Token == request.RefreshToken);

            if (existing == null || existing.IsRevoked)
            {
                return Results.BadRequest("Invalid token");
            }

            existing.IsRevoked = true;
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }

    // Extracted so both /login and /refresh can use it
    private static string GenerateJwt(User user, IList<string> roles, IConfiguration config)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!)
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static RefreshToken GenerateRefreshToken(string userId)
    {
        return new RefreshToken
        {
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        };
    }
}
