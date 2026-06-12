namespace PersonalFinance.Api.Services;

// Issues 15-min JWT access tokens and opaque refresh tokens.
// Refresh tokens are stored hashed; rotation revokes the old token and
// reuse of a revoked token kills the whole token family.
public class TokenService
{
}
