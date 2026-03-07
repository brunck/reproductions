namespace SyncfusionPickerMemoryLeak;

/// <summary>
/// Minimal reproduction of production's DeviceDetailTabsPage.
/// Contains a PickerPage tab (with two HourMinutePicker instances) and a placeholder tab.
/// Pushed modally from MainPage and disposed via DisposeAndClearChildren on back navigation.
///
/// The key: MAUI does NOT auto-disconnect handlers for tab children when a modal is popped,
/// unlike pages on a NavigationPage stack. Manual DisconnectHandler() calls do NOT remove
/// entries from ThemeElement.elements, so entries accumulate unboundedly.
/// </summary>
public class DeviceDetailTabbedPage : TabbedPage
{
    public DeviceDetailTabbedPage()
    {
        Title = "Device Settings";

        Children.Add(new PickerPage { Title = "Timers" });
        Children.Add(new PlaceholderTabPage { Title = "Details" });

        Console.WriteLine($"[DeviceDetailTabbedPage] Constructed (0x{GetHashCode():X8}), elements={ThemeElementInspector.GetElementsCount()}");
    }

    /// <summary>
    /// Mirrors production's BaseTabbedPage.DisposeAllPageScopesAndClearChildren().
    /// Disconnects handlers and removes children — but does NOT remove ThemeElement.elements entries.
    /// </summary>
    public void DisposeAndClearChildren()
    {
        Console.WriteLine($"[DeviceDetailTabbedPage] DisposeAndClearChildren (0x{GetHashCode():X8}), elements={ThemeElementInspector.GetElementsCount()}");

        foreach (var child in Children.ToList())
        {
            child.BindingContext = null;
            child.Handler?.DisconnectHandler();
            Children.Remove(child);
        }

        Console.WriteLine($"[DeviceDetailTabbedPage] After cleanup, elements={ThemeElementInspector.GetElementsCount()}");
    }
}

/// <summary>
/// Simple placeholder second tab — demonstrates multi-tab structure without adding pickers.
/// </summary>
public class PlaceholderTabPage : ContentPage
{
    public PlaceholderTabPage()
    {
        Content = new Label
        {
            Text = "Placeholder tab",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
    }
}
