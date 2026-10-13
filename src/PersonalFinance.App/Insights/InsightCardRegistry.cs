namespace PersonalFinance.App.Insights;

// Which view draws which card ViewModel. Filled by AddInsightCard, read by
// InsightCardTemplateSelector.
public record InsightCardRegistration(Type ViewModelType, Type ViewType);

public static class InsightCardServiceCollectionExtensions
{
    // The one line a new card needs in MauiProgram. Cards appear in
    // registration order within their tab.
    public static IServiceCollection AddInsightCard<TViewModel, TView>(this IServiceCollection services)
        where TViewModel : InsightCardViewModel
        where TView : View, new()
    {
        services.AddTransient<InsightCardViewModel, TViewModel>();
        services.AddSingleton(new InsightCardRegistration(typeof(TViewModel), typeof(TView)));
        return services;
    }
}

public class InsightCardTemplateSelector : DataTemplateSelector
{
    private readonly Dictionary<Type, DataTemplate> _templates;

    public InsightCardTemplateSelector(IEnumerable<InsightCardRegistration> registrations)
    {
        _templates = registrations.ToDictionary(r => r.ViewModelType, r => new DataTemplate(r.ViewType));
    }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
        _templates.TryGetValue(item.GetType(), out var template)
            ? template
            : throw new InvalidOperationException($"No view registered for {item.GetType().Name} - use AddInsightCard.");
}
