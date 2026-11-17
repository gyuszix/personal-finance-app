using PersonalFinance.App.ViewModels;

namespace PersonalFinance.App.Views;

public partial class SecurityPage : ContentPage
{
    private readonly SecurityViewModel _viewModel;

    public SecurityPage(SecurityViewModel viewModel)
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
