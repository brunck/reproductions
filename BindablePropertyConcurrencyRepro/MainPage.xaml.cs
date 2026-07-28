using System.Globalization;
using System.Reflection;

namespace BindablePropertyConcurrencyRepro;

public partial class MainPage : ContentPage
{
	/// <summary>How long a scenario runs before it is declared clean.</summary>
	static readonly TimeSpan RunDuration = TimeSpan.FromSeconds(30);

	readonly StressViewModel _viewModel = new();

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
	/// A1 — Reduced, no platform interaction. Two background loops writing two different custom
	/// bindable properties on the same element, alternating values so <c>willFirePropertyChanged</c>
	/// is true and <c>_pendingHandlerUpdatesFromBPSet.Add</c> is reached on both threads.
	///
	/// Neither property is in any platform property mapper, so no platform view is touched on any
	/// thread (see <see cref="RaceLabel"/>). The only shared mutable state in play is the
	/// framework's own <c>HashSet</c>.
	///
	/// This is the isolation, NOT the complaint. Writing bindable properties concurrently is the
	/// app's mistake; the complaint is that the framework corrupts its own state when an app makes
	/// that mistake, and reports it as an exception naming neither the property nor the thread.
	/// </summary>
	Task RunScenarioA1(RunContext run)
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
	/// A2 — Same shape, but against platform-mapped properties (<c>Opacity</c>,
	/// <c>CharacterSpacing</c>) — our production shape.
	///
	/// Included to document why A1 has to use custom properties: here
	/// <c>Element.UpdateHandlerValue</c> reaches the platform view and Android's
	/// <c>ViewRootImpl.checkThread</c> throws immediately, masking the framework defect behind a
	/// platform thread-affinity error.
	/// </summary>
	Task RunScenarioA2(RunContext run)
	{
		var target = TargetLabel;

		var opacity = run.Background(token =>
		{
			for (var i = 0; !token.IsCancellationRequested; i++)
				target.Opacity = i % 2 == 0 ? 1.0 : 0.99;
		});

		var spacing = run.Background(token =>
		{
			for (var i = 0; !token.IsCancellationRequested; i++)
				target.CharacterSpacing = i % 2 == 0 ? 0 : 1;
		});

		return Task.WhenAll(opacity, spacing);
	}

	/// <summary>
	/// B — Realistic. <c>Label.Text</c> bound to a plain-INPC view model whose notifications are
	/// raised from a background loop, while a fade animation runs on the UI thread against that
	/// same element. This is our production shape.
	///
	/// An experiment, not a known crash: MAUI marshals off-thread binding applies to the UI
	/// thread, so this may well stay clean.
	/// </summary>
	Task RunScenarioB(RunContext run)
	{
		TargetLabel.BindingContext = _viewModel;
		TargetLabel.SetBinding(Label.TextProperty, new Binding(nameof(StressViewModel.Text)));

		var notifier = run.Background(token =>
		{
			for (var i = 0; !token.IsCancellationRequested; i++)
				_viewModel.Text = i.ToString(CultureInfo.InvariantCulture);
		});

		return Task.WhenAll(notifier, run.Foreground(token => FadeLoopAsync(TargetLabel, token)));
	}

	/// <summary>
	/// C — Custom BP callback. A bound view-model property drives <see cref="RaceControl.IsBusy"/>,
	/// whose <c>propertyChanged</c> callback writes <c>BackgroundColor</c> on the same element,
	/// while a fade animation runs on it. Nested writes with two different property names against
	/// one shared set.
	/// </summary>
	Task RunScenarioC(RunContext run)
	{
		TargetControl.BindingContext = _viewModel;
		TargetControl.SetBinding(RaceControl.IsBusyProperty, new Binding(nameof(StressViewModel.IsBusy)));

		var toggler = run.Background(token =>
		{
			for (var i = 0; !token.IsCancellationRequested; i++)
				_viewModel.IsBusy = i % 2 == 0;
		});

		return Task.WhenAll(toggler, run.Foreground(token => FadeLoopAsync(TargetControl, token)));
	}

	/// <summary>
	/// D — Unmarshalled framework path. <see cref="VisualStateManager.GoToState"/> and a
	/// <see cref="Style"/> assignment, both applied from background threads. Neither path
	/// marshals to the UI thread, and both are commonly driven from view-model state, so this is
	/// the most plausible legitimate framework gap of the scenarios here.
	/// </summary>
	Task RunScenarioD(RunContext run)
	{
		var target = TargetLabel;

		// Resolve the styles on the UI thread; only the assignment happens off-thread.
		var styleA = (Style)Resources["RaceStyleA"];
		var styleB = (Style)Resources["RaceStyleB"];

		var states = run.Background(token =>
		{
			for (var i = 0; !token.IsCancellationRequested; i++)
				VisualStateManager.GoToState(target, i % 2 == 0 ? "Off" : "On");
		});

		var styles = run.Background(token =>
		{
			for (var i = 0; !token.IsCancellationRequested; i++)
				target.Style = i % 2 == 0 ? styleA : styleB;
		});

		return Task.WhenAll(states, styles, run.Foreground(token => FadeLoopAsync(target, token)));
	}

	// ---------------------------------------------------------------- plumbing

	void OnRunA1(object sender, EventArgs e) => _ = RunAsync("A1 (custom BPs)", RunScenarioA1);

	void OnRunA2(object sender, EventArgs e) => _ = RunAsync("A2 (mapped BPs)", RunScenarioA2);

	void OnRunB(object sender, EventArgs e) => _ = RunAsync("B (realistic)", RunScenarioB);

	void OnRunC(object sender, EventArgs e) => _ = RunAsync("C (custom BP callback)", RunScenarioC);

	void OnRunD(object sender, EventArgs e) => _ = RunAsync("D (VSM / Style)", RunScenarioD);

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
				ExceptionReporter.Report(source, run.Failure);
				StatusLabel.Text = $"{name}: THREW after {run.FailureAt.TotalSeconds:F2}s — " +
					$"{run.Failure.GetType().FullName}";
				DetailLabel.Text = ExceptionReporter.Describe(source, run.Failure);
			}
		}
		finally
		{
			run.Cancel();
			_run = null;
		}
	}

	/// <summary>Puts the shared elements back to a known state between scenarios.</summary>
	void ResetTarget()
	{
		TargetLabel.RemoveBinding(Label.TextProperty);
		TargetControl.RemoveBinding(RaceControl.IsBusyProperty);

		TargetLabel.Style = (Style)Resources["RaceStyleA"];
		TargetLabel.Text = "shared target element";
		TargetLabel.Opacity = 1;
		TargetLabel.CharacterSpacing = 0;
		TargetLabel.RaceAlpha = 0;
		TargetLabel.RaceBeta = 0;

		TargetControl.Opacity = 1;
		TargetControl.ClearValue(BackgroundColorProperty);
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
