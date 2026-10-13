using CommunityToolkit.Mvvm.ComponentModel;

namespace PersonalFinance.App.Insights;

// One Dashboard card. Subclass InsightCardViewModel<TResponse> for a card
// backed by a GET /insights/{key} endpoint, then register it with its view
// in MauiProgram: builder.Services.AddInsightCard<MyCard, MyCardView>().
//
// Each card loads, fails and goes empty on its own - one broken metric
// doesn't blank the Dashboard.
public abstract partial class InsightCardViewModel : ObservableObject
{
    private int _loadVersion;

    public abstract string Title { get; }
    public abstract InsightGroup Group { get; }

    // Shown when a load succeeds with nothing to draw.
    public virtual string EmptyMessage => "Nothing to show for this month yet.";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private bool hasData;

    // month is the first day of the month the Dashboard is showing.
    public async Task LoadAsync(DateTime month)
    {
        var version = ++_loadVersion;
        IsLoading = true;
        HasError = false;

        bool? hasData;
        try
        {
            hasData = await LoadCoreAsync(month);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Insight {GetType().Name}] {ex.GetType().Name}");
            hasData = null;
        }

        // A newer load (another month, a Refresh) started meanwhile; its
        // result wins.
        if (version != _loadVersion) return;

        IsLoading = false;
        HasError = hasData == null;
        HasData = hasData == true;
        IsEmpty = hasData == false;
    }

    // Fetch and apply the card's data. Returns whether there's anything to
    // show, or null if the load failed.
    protected abstract Task<bool?> LoadCoreAsync(DateTime month);
}

public abstract class InsightCardViewModel<TResponse>(InsightClient client) : InsightCardViewModel
    where TResponse : class
{
    // The endpoint's IInsight.Key on the server, e.g. "spending-by-category".
    protected abstract string Key { get; }

    protected override async Task<bool?> LoadCoreAsync(DateTime month)
    {
        var response = await client.GetAsync<TResponse>(Key, month);
        return response == null ? null : Apply(response);
    }

    // Turns the response into bindable properties. Returns whether there's
    // anything to show.
    protected abstract bool Apply(TResponse response);
}
