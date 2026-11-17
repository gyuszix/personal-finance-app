using PersonalFinance.App.ViewModels;

namespace PersonalFinance.App.Views;

public partial class RulesPage : ContentPage
{
    private readonly RulesViewModel _viewModel;

    public RulesPage(RulesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
