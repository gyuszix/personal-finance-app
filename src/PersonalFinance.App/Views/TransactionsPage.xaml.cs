using PersonalFinance.App.ViewModels;

namespace PersonalFinance.App.Views;

public partial class TransactionsPage : ContentPage
{
    public TransactionsPage(TransactionsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}