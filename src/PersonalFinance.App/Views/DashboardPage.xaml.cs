using PersonalFinance.App.Insights;
using PersonalFinance.App.ViewModels;

namespace PersonalFinance.App.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel, InsightCardTemplateSelector cardTemplates)
    {
        InitializeComponent();
        BindableLayout.SetItemTemplateSelector(Cards, cardTemplates);
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadDashboardCommand.Execute(null);
    }
}
