using PersonalFinance.App.Insights;
using PersonalFinance.App.ViewModels;

namespace PersonalFinance.App.Views;

public partial class DashboardPage : ContentPage
{
    // Below this a card's charts get cramped, so fewer, wider columns.
    private const double MinCardWidth = 400;
    private const int MaxColumns = 3;
    private const double Gap = 20;

    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel, InsightCardTemplateSelector cardTemplates)
    {
        InitializeComponent();
        BindableLayout.SetItemTemplateSelector(Cards, cardTemplates);
        BindingContext = _viewModel = viewModel;

        Cards.SizeChanged += (_, _) => LayOutCards();
        Cards.ChildAdded += (_, _) => LayOutCards();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadDashboardCommand.Execute(null);
    }

    // Splits the available width into 1-3 equal columns. A FlexLayout wraps
    // its children but doesn't size them, so each card gets its width here.
    private void LayOutCards()
    {
        var width = Cards.Width;
        if (width <= 0) return;

        var columns = (int)Math.Clamp(Math.Floor((width + Gap) / (MinCardWidth + Gap)), 1, MaxColumns);
        var cardWidth = Math.Floor((width - Gap * (columns - 1)) / columns);

        for (var i = 0; i < Cards.Children.Count; i++)
        {
            if (Cards.Children[i] is not View card) continue;
            var lastInRow = (i + 1) % columns == 0;
            card.WidthRequest = cardWidth;
            card.Margin = new Thickness(0, 0, lastInRow ? 0 : Gap, Gap);
        }
    }
}
