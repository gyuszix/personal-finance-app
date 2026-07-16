using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using Microsoft.AspNetCore.Identity;
using PersonalFinance.Api.Entities;

var builder = WebApplication.CreateBuilder(args);

// Adds OpenAPI/Swagger doc generation
builder.Services.AddOpenApi();

// Registers AppDbContext with DI, tells it to use Postgres with our connection string
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Registers Identity using our User class, and tells it to store data in AppDbContext
builder.Services.AddIdentityApiEndpoints<User>().AddEntityFrameworkStores<AppDbContext>();

var app = builder.Build();

// Maps built-in Identity endpoints (register, login, etc) to our User class
app.MapIdentityApi<User>();

if (app.Environment.IsDevelopment())
{
    // Serves the OpenAPI spec in development
    app.MapOpenApi();
}

// Redirects HTTP requests to HTTPS
app.UseHttpsRedirection();

app.Run();