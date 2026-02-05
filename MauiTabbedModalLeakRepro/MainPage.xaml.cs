namespace MauiTabbedModalLeakRepro;

public partial class MainPage
{
	private bool _isRunning;
	private const int DefaultIterations = 5;

	public MainPage()
	{
		InitializeComponent();
		IterationsStepper.Value = DefaultIterations;
		IterationsLabel.Text = DefaultIterations.ToString();
		StatusLabel.Text = "Idle";
	}

	private void OnIterationsChanged(object? sender, ValueChangedEventArgs e)
	{
		IterationsLabel.Text = ((int)e.NewValue).ToString();
	}

	private async void OnRunOnceClicked(object? sender, EventArgs e)
    {
        try
        {
            await RunReproAsync(1);
        }
        catch (Exception ex)
        {
			ReproLog.Log($"Error running once: {ex}");
        }
    }

	private async void OnRunNTimesClicked(object? sender, EventArgs e)
    {
        try
        {
            await RunReproAsync((int)IterationsStepper.Value);
        }
        catch (Exception ex)
        {
            ReproLog.Log($"Error running N times: {ex}");
        }
    }

	private void OnClearTrackedClicked(object? sender, EventArgs e)
	{
		LeakTracker.Clear();
		StatusLabel.Text = "Cleared tracked WeakReferences.";
	}

	private void OnForceGcClicked(object? sender, EventArgs e)
	{
		try
		{
			StatusLabel.Text = "Collecting before/after GC snapshots...";
			var before = LeakTracker.GetSnapshotWithoutGc("before GC");
			var after = LeakTracker.ForceGcAndGetSnapshot("after GC");
			var display = before.ToDisplayString() + "\n" + after.ToDisplayString();
			StatusLabel.Text = display;
			ReproLog.Log("Force GC snapshot:\n" + display);
		}
		catch (Exception ex)
		{
			StatusLabel.Text = "Error during GC: " + ex.Message;
			ReproLog.Log("Error during Force GC: " + ex);
		}
	}

	private async Task RunReproAsync(int iterations)
	{
		if (_isRunning)
		{
			return;
		}

		try
		{
			_isRunning = true;
			SetButtonsEnabled(false);
			ReproLog.Log($"Starting repro loop; iterations={iterations}");

			for (var i = 1; i <= iterations; i++)
			{
				await RunOneIterationAsync(i, iterations);

				// Important: don't force GC while the modal page is still referenced by locals
				// that are hoisted into an async state machine (a common false-positive).
				await Task.Yield();

				var snapshot = LeakTracker.ForceGcAndGetSnapshot($"after pop {i}/{iterations}");
				StatusLabel.Text = snapshot.ToDisplayString();
				ReproLog.Log(snapshot.ToDisplayString());
			}

			ReproLog.Log("Repro loop finished.");
		}
		finally
		{
			_isRunning = false;
			SetButtonsEnabled(true);
		}
	}

	private async Task RunOneIterationAsync(int iteration, int iterations)
	{
		var wrapTabs = WrapTabsSwitch?.IsToggled ?? false;
		var page = new LeakyTabbedModalPage(wrapTabs);
		LeakTracker.TrackTabbedPage(page);

		ReproDiagnostics.LogNavigationState(this, $"before push {iteration}/{iterations}; wrapTabs={wrapTabs}");
		await Navigation.PushModalAsync(page, animated: false);
		await Task.Delay(150);

		page.SwitchToSettingsTab();
		await Task.Delay(150);

		ReproDiagnostics.LogNavigationState(this, $"before pop {iteration}/{iterations}");
		await Navigation.PopModalAsync(animated: false);
		await Task.Delay(100);
	}

	private void SetButtonsEnabled(bool enabled)
	{
		foreach (var view in ((VerticalStackLayout)((ScrollView)Content).Content).Children)
		{
			if (view is Button button)
			{
				button.IsEnabled = enabled;
			}
		}
	}
}
