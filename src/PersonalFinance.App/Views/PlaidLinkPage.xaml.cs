using System.Text.Json;

namespace PersonalFinance.App.Views;

// Hosts Plaid's Link web SDK in a WebView. Works the same on every MAUI
// platform without a native SDK dependency - the JS callbacks navigate to a
// plaidlink:// URL that OnNavigating intercepts instead of ever loading.
public partial class PlaidLinkPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _completion = new();

    public PlaidLinkPage(string linkToken)
    {
        InitializeComponent();
        LinkWebView.Source = new HtmlWebViewSource { Html = BuildHtml(linkToken) };
    }

    public Task<string?> WaitForResultAsync() => _completion.Task;

    private async void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!e.Url.StartsWith("plaidlink://")) return;

        e.Cancel = true;
        var uri = new Uri(e.Url);

        switch (uri.Host)
        {
            case "success":
                _completion.TrySetResult(GetQueryParam(uri, "public_token"));
                break;
            case "error":
                _completion.TrySetException(
                    new InvalidOperationException(GetQueryParam(uri, "message") ?? "Plaid Link error"));
                break;
            default:
                _completion.TrySetResult(null);
                break;
        }

        await Navigation.PopModalAsync();
    }

    private static string? GetQueryParam(Uri uri, string key)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key)
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private static string BuildHtml(string linkToken)
    {
        var tokenLiteral = JsonSerializer.Serialize(linkToken);

        return $$"""
            <!DOCTYPE html>
            <html>
            <head><script src="https://cdn.plaid.com/link/v2/stable/link-initialize.js"></script></head>
            <body>
            <script>
                var handler = Plaid.create({
                    token: {{tokenLiteral}},
                    onSuccess: function (publicToken) {
                        window.location.href = 'plaidlink://success?public_token=' + encodeURIComponent(publicToken);
                    },
                    onExit: function (err) {
                        if (err) {
                            window.location.href = 'plaidlink://error?message=' + encodeURIComponent(err.error_message || err.error_code || 'unknown error');
                        } else {
                            window.location.href = 'plaidlink://cancel';
                        }
                    }
                });
                handler.open();
            </script>
            </body>
            </html>
            """;
    }
}
