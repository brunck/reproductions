using SfListViewResetLeakRepro.Diagnostics;

namespace SfListViewResetLeakRepro;

public partial class MainPage : ContentPage
{
	private bool _listEventHandlersAttached;

	public MainPage()
	{
        InitializeComponent();
		var viewModel = new MainViewModel();
		BindingContext = viewModel;
		AttachListEventHandlers();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		AttachListEventHandlers();
	}

	protected override void OnDisappearing()
	{
		DetachListEventHandlers();
		base.OnDisappearing();
	}

    private void AttachListEventHandlers()
	{
		if (_listEventHandlersAttached)
			return;

		try
		{
			LeakListView.ItemTapped += LeakListView_ItemTapped;
			LeakListView.ItemAppearing += LeakListView_ItemAppearing;
			LeakListView.ItemDragging += LeakListView_ItemDragging;
			LeakListView.ScrollStateChanged += LeakListView_ScrollStateChanged;
			_listEventHandlersAttached = true;
		}
		catch
		{
			// best-effort
		}
	}

	private void DetachListEventHandlers()
	{
		if (!_listEventHandlersAttached)
        {
            return;
        }

        try { LeakListView.ItemTapped -= LeakListView_ItemTapped; } catch { }
		try { LeakListView.ItemAppearing -= LeakListView_ItemAppearing; } catch { }
		try { LeakListView.ItemDragging -= LeakListView_ItemDragging; } catch { }
		try { LeakListView.ScrollStateChanged -= LeakListView_ScrollStateChanged; } catch { }
		_listEventHandlersAttached = false;
	}

	private void LeakListView_ItemTapped(object? sender, Syncfusion.Maui.ListView.ItemTappedEventArgs e) { }
	private void LeakListView_ItemAppearing(object? sender, Syncfusion.Maui.ListView.ItemAppearingEventArgs e) { }
	private void LeakListView_ItemDragging(object? sender, Syncfusion.Maui.ListView.ItemDraggingEventArgs e) { }
	private void LeakListView_ScrollStateChanged(object? sender, Syncfusion.Maui.ListView.ScrollStateChangedEventArgs e) { }

    private void OnFooterTapped(object? sender, TappedEventArgs e)
	{
		// Intentionally empty.
	}

	private async void OnListDiagnosticsClicked(object? sender, EventArgs e)
	{
		try
		{
			// Get concise summary string, log each line, and show it in the popup.
			var summary = SfListViewCacheProbe.GetTemplateViewCacheSummary(LeakListView);

			// Write to console so it'll appear in logcat/stdout.
			foreach (var raw in summary.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				try { Console.WriteLine(raw); } catch { }
			}

			// Also show the concise summary to the user in the alert popup.
			await DisplayAlert("SfListView", string.IsNullOrWhiteSpace(summary) ? "(no results)" : summary, "OK");
		}
		catch (Exception ex)
		{
			await DisplayAlert("SfListView", $"Probe failed: {ex.GetType().Name}: {ex.Message}", "OK");
		}
	}
}
