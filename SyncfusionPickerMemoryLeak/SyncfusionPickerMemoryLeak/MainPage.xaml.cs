namespace SyncfusionPickerMemoryLeak;

public partial class MainPage : ContentPage
{
    private readonly List<WeakReference<Page>> _pageRefs = [];
    private int _cycleCount;
    private bool _autoRunning;
    private int? _baseline;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnNavigateClicked(object? sender, EventArgs e)
    {
        await PushModalCycle();
    }

    private void OnForceGcClicked(object? sender, EventArgs e)
    {
        ForceGc();
        ResultsLabel.Text = $"GC forced after {_cycleCount} cycle(s). Tap 'Inspect' to check.";
        Console.WriteLine($"[MainPage] GC forced");
    }

    private void OnInspectClicked(object? sender, EventArgs e)
    {
        var text = BuildInspectText();
        ResultsLabel.Text = text;
        Console.WriteLine($"[MainPage] === Inspect ===\n{text}\n=== End ===");
    }

    private async void OnAutoRunClicked(object? sender, EventArgs e)
    {
        if (_autoRunning) return;
        _autoRunning = true;
        AutoRunButton.IsEnabled = false;
        NavigateButton.IsEnabled = false;

        const int cycles = 5;

        try
        {
            _baseline = ThemeElementInspector.GetElementsCount();
            ResultsLabel.Text = $"Baseline: {_baseline} elements. Starting {cycles} cycles...";
            Console.WriteLine($"[AutoRun] Baseline: {_baseline} elements");

            for (int i = 0; i < cycles; i++)
            {
                ResultsLabel.Text = $"Cycle {i + 1}/{cycles}: pushing modal...";
                await PushModalCycle();
                await Task.Delay(200);
            }

            ResultsLabel.Text = "All cycles done. Forcing GC...";
            Console.WriteLine("[AutoRun] Forcing GC...");
            ForceGc();
            await Task.Delay(500);

            var text = BuildInspectText();
            ResultsLabel.Text = text;
            Console.WriteLine($"[AutoRun] === Final Inspection ===\n{text}\n=== End ===");
        }
        finally
        {
            _autoRunning = false;
            AutoRunButton.IsEnabled = true;
            NavigateButton.IsEnabled = true;
        }
    }

    private async Task PushModalCycle()
    {
        var countBefore = ThemeElementInspector.GetElementsCount();
        Console.WriteLine($"[MainPage] Cycle {_cycleCount + 1}: elements BEFORE push={countBefore}");

        var tabbedPage = new DeviceDetailTabbedPage();
        _pageRefs.Add(new WeakReference<Page>(tabbedPage));
        _cycleCount++;
        CycleCountLabel.Text = $"Navigation cycles: {_cycleCount}";

        // Push modal TabbedPage — MAUI does NOT auto-disconnect handlers on modal pop
        // the way it does for pages on a NavigationPage stack. This is the key difference.
        await Navigation.PushModalAsync(new NavigationPage(tabbedPage));
        await Task.Delay(500); // let the page appear and render

        var countAfterPush = ThemeElementInspector.GetElementsCount();
        Console.WriteLine($"[MainPage] Cycle {_cycleCount}: elements AFTER push={countAfterPush} (+{countAfterPush - countBefore})");

        // Simulate user going back: run cleanup then pop.
        // DisposeAndClearChildren mirrors production's BaseTabbedPage cleanup,
        // which disconnects handlers but does NOT remove ThemeElement.elements entries.
        tabbedPage.DisposeAndClearChildren();
        await Navigation.PopModalAsync();
        await Task.Delay(300);

        var countAfterPop = ThemeElementInspector.GetElementsCount();
        Console.WriteLine($"[MainPage] Cycle {_cycleCount}: elements AFTER pop={countAfterPop} (net: +{countAfterPop - countBefore})");
    }

    private string BuildInspectText()
    {
        var count = ThemeElementInspector.GetElementsCount();
        var byType = ThemeElementInspector.GetElementsByType();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ThemeElement.elements count: {count}");

        if (_baseline.HasValue)
        {
            var delta = count - _baseline.Value;
            sb.AppendLine($"Delta from baseline: {(delta >= 0 ? "+" : "")}{delta} (baseline={_baseline.Value})");
        }

        sb.AppendLine();

        if (byType.Count > 0)
        {
            sb.AppendLine("By type:");
            foreach (var kv in byType)
                sb.AppendLine($"  {kv.Value,4}x  {kv.Key}");
        }
        else
        {
            sb.AppendLine("(elements list is empty)");
        }

        sb.AppendLine();
        sb.AppendLine($"WeakRefs ({_pageRefs.Count} pages tracked):");
        int alive = 0;
        for (int i = 0; i < _pageRefs.Count; i++)
        {
            bool isAlive = _pageRefs[i].TryGetTarget(out _);
            if (isAlive) alive++;
            sb.AppendLine($"  [{i + 1}] {(isAlive ? "ALIVE (leaked!)" : "collected (OK)")}");
        }

        sb.AppendLine();
        sb.AppendLine($"Pages alive after GC: {alive}/{_pageRefs.Count}");

        if (_baseline.HasValue)
        {
            var delta = count - _baseline.Value;
            sb.AppendLine();
            if (delta > 0 && alive > 0)
                sb.AppendLine("=> LEAK CONFIRMED: elements grew AND pages retained.");
            else if (delta > 0)
                sb.AppendLine("=> STATIC LEAK: elements grew but pages were GC'd.");
            else if (alive > 0)
                sb.AppendLine("=> PAGE LEAK: pages retained but elements stable.");
            else
                sb.AppendLine("=> No leak detected in this run.");
        }

        return sb.ToString().TrimEnd();
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
