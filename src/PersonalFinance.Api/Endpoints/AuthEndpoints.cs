using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using PersonalFinance.Api.DTOs;
using PersonalFinance.Api.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PersonalFinance.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        // Temporary test route — proves JWT validation works
        // Replace with real protected endpoints later (e.g. GET /accounts)
        app.MapGet("/protected", () => Results.Ok("you are authenticated"))
           .RequireAuthorization();

        // Admin-only test route — proves role-based authorisation works
        app.MapGet("/admin", () => Results.Ok("you are an admin"))
           .RequireAuthorization(policy => policy.RequireRole("Admin"));

        // Creates a new user — Identity hashes the password and saves to Postgres
        // Automatically assigns the "User" role to every new registration
        app.MapPost("/auth/register", async (
            RegisterRequest request,
            UserManager<User> userManager) =>
        {
            var user = new User { UserName = request.Email, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);

            if (result.Succeeded)
            {
                // Every new user gets the "User" role by default
                await userManager.AddToRoleAsync(user, "User");
                return Results.Ok("User registered successfully");
            }

            return Results.BadRequest(result.Errors);
        });

        // Validates credentials and returns a signed JWT on success
        // JWT payload includes the user's role so authorisation works on protected routes
        app.MapPost("/auth/login", async (
            LoginRequest request,
            UserManager<User> userManager,
            IConfiguration config) =>
        {
            // Check user exists and password is correct
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user == null || !await userManager.CheckPasswordAsync(user, request.Password))
                return Results.Unauthorized();

            // Bake user identity into the token payload
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!)
            };

            // Fetch the user's roles and add them as claims so the token carries role info
            var roles = await userManager.GetRolesAsync(user);
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            // Sign the token with our secret key
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Build the token with issuer, audience, claims and 1hr expiry
            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds
            );

            return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
        });
    }
}