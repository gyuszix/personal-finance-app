namespace PersonalFinance.Api.Services.Auth;

public class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    // Short-lived: a stolen access token is only good this long, since
    // access tokens can't be revoked (#115).
    public int AccessTokenMinutes { get; set; } = 10;

    // Each refresh token's lifetime; rotation issues a fresh one...
    public int RefreshTokenDays { get; set; } = 7;

    // ...but never past this long after the login that started the family.
    public int RefreshFamilyDays { get; set; } = 30;
}
