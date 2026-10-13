using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace PersonalFinance.App.Insights;

// GET /api/v1/insights/{key}?month=yyyy-MM. Shares AuthRefreshHandler with
// the Refit client, so tokens and refresh-on-401 behave the same.
public class InsightClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // Null on any failure, same as ApiService.
    public async Task<T?> GetAsync<T>(string key, DateTime month) where T : class
    {
        var url = $"api/v1/insights/{Uri.EscapeDataString(key)}?month={month.ToString("yyyy-MM", CultureInfo.InvariantCulture)}";
        try
        {
            using var response = await http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<T>(Json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }
}
