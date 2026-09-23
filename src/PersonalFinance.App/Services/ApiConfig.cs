namespace PersonalFinance.App.Services;

public static class ApiConfig
{
    private const string DefaultBaseUrl = "http://localhost:5140";

    // Overridable at launch so the app can be pointed at a non-local API
    // without a rebuild. Deliberately not a const: a const would be inlined
    // into every consuming assembly at compile time.
    public static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("PERSONALFINANCE_API_URL") is { Length: > 0 } url
            ? url
            : DefaultBaseUrl;
}
