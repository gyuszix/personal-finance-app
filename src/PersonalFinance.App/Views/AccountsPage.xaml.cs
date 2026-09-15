using PersonalFinance.App.ViewModels;

namespace PersonalFinance.App.Views;

public partial class AccountsPage : ContentPage
{
    public AccountsPage(AccountsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
