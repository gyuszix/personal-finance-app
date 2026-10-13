namespace PersonalFinance.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Pushed from the budget variance card, not a tab
		Routing.RegisterRoute("budgets", typeof(Views.BudgetsPage));
	}
}
