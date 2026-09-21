using PersonalFinance.App.Services;

namespace PersonalFinance.App;

public partial class App : Application
{
	private readonly ApiService _apiService;

	public App(ApiService apiService)
	{
		InitializeComponent();
		_apiService = apiService;

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
		var shell = new AppShell();

		// If a session was persisted from a previous run, skip straight past
		// the login page instead of always starting there.
		shell.Loaded += async (_, _) =>
		{
			if (await _apiService.TryRestoreSessionAsync())
				await shell.GoToAsync("//dashboard");
		};

		return new Window(shell);
	}
}