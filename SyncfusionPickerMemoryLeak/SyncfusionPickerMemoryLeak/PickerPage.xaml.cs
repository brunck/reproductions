using Syncfusion.Maui.Picker;

namespace SyncfusionPickerMemoryLeak;

/// <summary>
/// Mirrors production's DeviceSettingsPage: two HourMinutePicker instances (dialog mode)
/// with ViewModel binding and SelectionChanged event handlers that capture 'this'.
///
/// Reference chain that prevents GC:
/// ThemeElement.elements (static) → PickerColumnHeaderView → event handler → HourMinutePicker
///   → NameScope → PickerPage → PickerViewModel
/// </summary>
public partial class PickerPage : ContentPage
{
    private readonly PickerViewModel _viewModel;

    public PickerPage()
    {
        InitializeComponent();

        _viewModel = new PickerViewModel();
        BindingContext = _viewModel;

        // Mirrors production: ViewModel PropertyChanged subscription creates strong ref chain
        // ViewModel -> PropertyChanged delegate -> PickerPage
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Console.WriteLine($"[PickerPage] Constructed (0x{GetHashCode():X8}), elements={ThemeElementInspector.GetElementsCount()}");
    }

    private void OnOverrideSelectionChanged(object? sender, PickerSelectionChangedEventArgs e)
    {
        _viewModel.UpdateOverrideTimeDisplay(OverrideTimePicker.GetSelectedHour(), OverrideTimePicker.GetSelectedMinute());
    }

    private void OnServiceSelectionChanged(object? sender, PickerSelectionChangedEventArgs e)
    {
        _viewModel.UpdateServiceTimeDisplay(ServiceTimePicker.GetSelectedHour(), ServiceTimePicker.GetSelectedMinute());
    }

    private void OnOverrideOpenClicked(object? sender, EventArgs e)
    {
        OverrideTimePicker.IsOpen = !OverrideTimePicker.IsOpen;
    }

    private void OnServiceOpenClicked(object? sender, EventArgs e)
    {
        ServiceTimePicker.IsOpen = !ServiceTimePicker.IsOpen;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Keeps reference chain: ViewModel -> PropertyChanged -> PickerPage -> pickers
        Console.WriteLine($"[PickerPage] ViewModel.{e.PropertyName} changed");
    }
}
