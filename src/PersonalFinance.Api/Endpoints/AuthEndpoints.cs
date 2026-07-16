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

        // Creates a new user — Identity hashes the password and saves to Postgres
        app.MapPost("/auth/register", async (
            RegisterRequest request,
            UserManager<User> userManager) =>
        {
            var user = new User { UserName = request.Email, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);

            return result.Succeeded
                ? Results.Ok("User registered successfully")
                : Results.BadRequest(result.Errors);
        });

        // Validates credentials and returns a signed JWT on success
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
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!)
            };

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