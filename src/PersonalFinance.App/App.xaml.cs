namespace PersonalFinance.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			System.Diagnostics.Debug.WriteLine($"[UnhandledException] {e.ExceptionObject}");

		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			System.Diagnostics.Debug.WriteLine($"[UnobservedTaskException] {e.Exception}");
			e.SetObserved();
		};
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}