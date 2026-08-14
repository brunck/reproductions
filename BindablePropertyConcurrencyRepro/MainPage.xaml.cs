using System.Reflection;

namespace BindablePropertyConcurrencyRepro;

public partial class MainPage : ContentPage
{
	/// <summary>How long a scenario runs before it is declared clean.</summary>
	static readonly TimeSpan RunDuration = TimeSpan.FromSeconds(30);

	RunContext? _run;

	public MainPage()
	{
		InitializeComponent();

		EnvironmentLabel.Text = DescribeEnvironment();

		var previous = ExceptionReporter.ReadPreviousReports();
		if (!string.IsNullOrWhiteSpace(previous))
		{
			StatusLabel.Text = "A report from a previous run was captured (see below).";
			DetailLabel.Text = previous;
		}
	}

	// ---------------------------------------------------------------- scenarios

	/// <summary>
	/// A — Two background loops writing two different custom bindable properties on the same
	/// element, alternating values so <c>willFirePropertyChanged</c> is true and
	/// <c>_pendingHandlerUpdatesFromBPSet.Add</c> is reached on both threads.
	///
	/// Neither property is in any platform property mapper, so no platform view is touched on any
	/// thread (see <see cref="RaceLabel"/>). The only shared mutable state in play is the
	/// framework's own <c>HashSet</c>.
	///
	/// This isolates the mechanism. Writing bindable properties concurrently is the app's
	/// mistake; the complaint is that the framework corrupts its own state when an app makes that
	/// mistake, and reports it as an exception naming neither the property nor the thread.
	/// </summary>
	Task RunScenarioA(RunContext run)
	{
		var target = TargetLabel;

		var alpha = run.Background(token =>
		{
			for (var i = 0; !token.IsCancellationRequested; i++)
				target.RaceAlpha = i;
		});

		var beta = run.Background(token =>
		{
			for (var i = 0; !token.IsCancellationRequested; i++)
				target.RaceBeta = -i;
		});

		return Task.WhenAll(alpha, beta);
	}

	/// <summary>
	/// B — Unmarshalled framework path. <b>One</b> background loop calling
	/// <see cref="VisualStateManager.GoToState"/>, racing ordinary UI-thread work (a fade
	/// animation) on the same element.
	///
	/// This is the load-bearing scenario for severity: the app calls exactly one off-thread API,
	/// and <c>GoToState</c> is not documented as UI-thread-only, is commonly driven from
	/// view-model state, and (unlike the binding engine) does not marshal. Everything else
	/// running is the framework's own animation ticker on the UI thread.
	///
	/// Both sides land in <c>Element.OnBindablePropertySet</c> on the same element:
	/// <c>GoToState</c> via <c>Setter.Apply</c>/<c>UnApply</c> writing and clearing
	/// <c>TextColor</c>, the animation via repeated <c>Opacity</c> writes.
	/// </summary>
	Task RunScenarioB(RunContext run)
	{
		var target = TargetLabel;

		var states = run.Background(token =>
		{
			for (var i = 0; !token.IsCancellationRequested; i++)
				VisualStateManager.GoToState(target, i % 2 == 0 ? "Off" : "On");
		});

		return Task.WhenAll(states, run.Foreground(token => FadeLoopAsync(target, token)));
	}

	// ---------------------------------------------------------------- plumbing

	void OnRunA(object sender, EventArgs e) => _ = RunAsync("A (custom BPs)", RunScenarioA);

	void OnRunB(object sender, EventArgs e) => _ = RunAsync("B (1 off-thread GoToState)", RunScenarioB);

	void OnStop(object sender, EventArgs e)
	{
		_run?.Cancel();
		StatusLabel.Text = "stopping…";
	}

	void OnClearReport(object sender, EventArgs e)
	{
		ExceptionReporter.ClearPreviousReports();
		DetailLabel.Text = string.Empty;
		StatusLabel.Text = "report cleared";
	}

	async Task RunAsync(string name, Func<RunContext, Task> scenario)
	{
		if (_run is not null)
		{
			StatusLabel.Text = "a scenario is already running — press Stop first";
			return;
		}

		ResetTarget();
		DetailLabel.Text = string.Empty;
		StatusLabel.Text = $"{name}: running up to {RunDuration.TotalSeconds:F0}s…";

		using var run = new RunContext(RunDuration);
		_run = run;

		try
		{
			await scenario(run);

			if (run.Failure is null)
			{
				StatusLabel.Text = $"{name}: CLEAN after {run.Elapsed.TotalSeconds:F1}s — no exception.";
			}
			else
			{
				var source = $"{name} threw after {run.FailureAt.TotalSeconds:F2}s";
				ExceptionReporter.Report(source, run.Failure, run.FailureThreadId);
				StatusLabel.Text = $"{name}: THREW after {run.FailureAt.TotalSeconds:F2}s — " +
					$"{run.Failure.GetType().FullName}";
				DetailLabel.Text = ExceptionReporter.Describe(source, run.Failure, run.FailureThreadId);
			}
		}
		finally
		{
			run.Cancel();
			_run = null;
		}
	}

	/// <summary>Puts the shared element back to a known state between scenarios.</summary>
	void ResetTarget()
	{
		TargetLabel.Style = (Style)Resources["RaceStyleA"];
		TargetLabel.Text = "shared target element";
		TargetLabel.Opacity = 1;
		TargetLabel.RaceAlpha = 0;
		TargetLabel.RaceBeta = 0;
	}

	static async Task FadeLoopAsync(VisualElement element, CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			await element.FadeToAsync(0.2, 250);
			await element.FadeToAsync(1.0, 250);
		}
	}

	static string DescribeEnvironment()
	{
		var assembly = typeof(Element).Assembly;
		var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
			?? assembly.GetName().Version?.ToString()
			?? "unknown";

		return $"Microsoft.Maui.Controls {version}\n" +
			$"{DeviceInfo.Platform} {DeviceInfo.VersionString} · {DeviceInfo.Manufacturer} {DeviceInfo.Model} · " +
			$"{Environment.ProcessorCount} cores";
	}
}
