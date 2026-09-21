using System.Text.Json;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using PersonalFinance.App.Services;
using PersonalFinance.App.ViewModels;
using PersonalFinance.App.Views;
using Refit;

namespace PersonalFinance.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // API client - Refit-generated, with a handler that attaches the
        // bearer token and transparently refreshes it on a 401.
        // AddRefitGeneratedClient (not AddRefitClient) is required here - the
        // plain reflection-based request builder AddRefitClient defaults to
        // isn't installed/AOT-safe and crashes at first resolution on Mac
        // Catalyst; the generated variant uses the compile-time source
        // generator's implementation instead.
        var refitSettings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            })
        };

        builder.Services.AddSingleton<AuthTokenProvider>();
        builder.Services.AddTransient<AuthRefreshHandler>();
        builder.Services
            .AddRefitGeneratedClient<IPersonalFinanceApi>(refitSettings)
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(ApiConfig.BaseUrl))
            .AddHttpMessageHandler<AuthRefreshHandler>();

        builder.Services.AddSingleton<ApiService>();

        // Plaid Link - WebView-based today, #32/#33 may swap in native SDKs later
        builder.Services.AddTransient<IPlaidLinkService, PlaidLinkWebService>();

        // Offline transaction cache
        builder.Services.AddSingleton<TransactionCacheService>();

        // ViewModels
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<TransactionsViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<AccountsViewModel>();

        // Views
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<TransactionsPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<AccountsPage>();

		//Regiester ViewModel
		builder.Services.AddTransient<RegisterViewModel>();
		builder.Services.AddTransient<RegisterPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}