using System.Reflection;

namespace BindablePropertyConcurrencyRepro;

public partial class MainPage : ContentPage
{
	/// <summary>
	/// How long a scenario runs before it is declared clean. Generously above the slowest throw
	/// observed on any device so far (0.7 s), but short enough that a run which somehow does *not*
	/// throw cannot spin the loops long enough to matter.
	/// </summary>
	static readonly TimeSpan RunDuration = TimeSpan.FromSeconds(10);

	/// <summary>How long past <see cref="RunDuration"/> to wait for the loops to unwind before
	/// reporting anyway. See the strand note in <see cref="RunAsync"/>.</summary>
	static readonly TimeSpan StrandGrace = TimeSpan.FromSeconds(2);

	RunContext? _run;

	public MainPage()
	{
		InitializeComponent();

		EnvironmentLabel.Text = DescribeEnvironment();

		// Attribute uncatchable throws (see RunContext.FailExternally) to the run in flight.
		ExceptionReporter.Unhandled += ex => _run?.FailExternally(ex);

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
	static Task RunScenarioA(RunContext run, RaceLabel target)
	{
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
	/// This carries the severity argument on Android: the app calls exactly one off-thread API,
	/// and <c>GoToState</c> is not documented as UI-thread-only, is commonly driven from
	/// view-model state, and (unlike the binding engine) does not marshal. Everything else
	/// running is the framework's own animation ticker on the UI thread.
	///
	/// Both sides land in <c>Element.OnBindablePropertySet</c> on the same element:
	/// <c>GoToState</c> via <c>Setter.Apply</c>/<c>UnApply</c> writing and clearing
	/// <c>TextColor</c>, the animation via repeated <c>Opacity</c> writes.
	///
	/// On iOS it argues something narrower. <c>TextColor</c> reaches <c>UILabel</c>, so UIKit's
	/// thread-affinity check kills this loop on its first iteration — enough to prove the check
	/// does not protect the collection (the write reaches the set before UIKit stops the call),
	/// but not enough traffic to corrupt it. There, <see cref="RunScenarioA"/> is what corrupts
	/// the set and the UI thread dies in the layout pass. See the iOS section of the README.
	/// </summary>
	static Task RunScenarioB(RunContext run, RaceLabel target)
	{
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

	async Task RunAsync(string name, Func<RunContext, RaceLabel, Task> scenario)
	{
		if (_run is not null)
		{
			StatusLabel.Text = "a scenario is already running — press Stop first";
			return;
		}

		var target = NewTarget();
		TargetHost.Content = target;

		DetailLabel.Text = string.Empty;
		StatusLabel.Text = $"{name}: running up to {RunDuration.TotalSeconds:F0}s…";

		var run = new RunContext(RunDuration);
		_run = run;

		try
		{
			var work = scenario(run, target);

			// The scenario can strand. When the throw lands in the animation ticker rather than in
			// app code, the fade's TaskCompletionSource is never completed and Task.WhenAll never
			// returns — so report on the run's own schedule rather than waiting for it, and leave
			// the RunContext undisposed until the stranded loops actually unwind.
			await Task.WhenAny(work, Task.Delay(RunDuration + StrandGrace));
			run.Cancel();
			_ = work.ContinueWith(static (_, state) => ((RunContext)state!).Dispose(), run,
				TaskScheduler.Default);

			if (run.Failure is null)
			{
				StatusLabel.Text = $"{name}: CLEAN after {run.Elapsed.TotalSeconds:F1}s — no exception.";
			}
			else
			{
				var kind = run.FailureWasUnhandled ? "THREW (unhandled)" : "THREW";
				var source = $"{name} {kind} after {run.FailureAt.TotalSeconds:F2}s";

				if (!run.FailureWasUnhandled)
					ExceptionReporter.Report(source, run.Failure, run.FailureThreadId);

				StatusLabel.Text = $"{name}: {kind} after {run.FailureAt.TotalSeconds:F2}s — " +
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

	/// <summary>
	/// Builds the element a run races against.
	///
	/// Every run gets a brand new one, and the previous run's element is dropped. Reusing a single
	/// element does not work: the race leaves the framework's per-element state corrupted, and a
	/// reused element behaves nothing like a fresh one — later runs stop throwing and instead
	/// retain memory until the process is killed. That is a downstream effect of the same defect,
	/// but it makes the repro read as flaky, so each run starts from a clean element and lets the
	/// damaged one become garbage.
	/// </summary>
	static RaceLabel NewTarget()
	{
		var target = new RaceLabel
		{
			Text = "target element (fresh for this run)",
			TextColor = OffColor,
		};

		// Off/On states for scenario B. Only TextColor differs, so applying them does not force a
		// platform layout pass; the point is the Setter -> SetValue traffic.
		var states = new VisualStateGroup { Name = "RaceStates" };
		states.States.Add(NewState("Off", OffColor));
		states.States.Add(NewState("On", OnColor));

		VisualStateManager.SetVisualStateGroups(target, new VisualStateGroupList { states });

		return target;
	}

	static VisualState NewState(string name, Color textColor)
	{
		var state = new VisualState { Name = name };
		state.Setters.Add(new Setter { Property = Label.TextColorProperty, Value = textColor });
		return state;
	}

	static readonly Color OffColor = Color.FromArgb("#1E88E5");
	static readonly Color OnColor = Color.FromArgb("#D81B60");

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
