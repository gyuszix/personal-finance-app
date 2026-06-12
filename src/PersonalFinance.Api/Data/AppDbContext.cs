namespace PersonalFinance.Api.Data;

// EF Core context (extends IdentityDbContext). Global query filter on UserId
// scopes every query to the current user by construction.
public class AppDbContext
{
}
