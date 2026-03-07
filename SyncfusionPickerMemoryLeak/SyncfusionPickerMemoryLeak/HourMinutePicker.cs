using System.Collections.ObjectModel;
using Syncfusion.Maui.Picker;

namespace SyncfusionPickerMemoryLeak;

/// <summary>
/// Custom SfPicker subclass mirroring production's HourMinutePicker.
///
/// Subclassing matters: internal children (PickerColumnHeaderView, PickerSelectionView, etc.)
/// register in ThemeElement.elements during construction. Their event handlers point back to
/// this instance, which holds the columns and ObservableCollections, creating the reference
/// chain that prevents GC when ThemeElement.elements holds a strong reference.
/// </summary>
public class HourMinutePicker : SfPicker
{
    private readonly PickerColumn _hourColumn;
    private readonly PickerColumn _minuteColumn;
    private readonly ObservableCollection<object> _hours;
    private readonly ObservableCollection<object> _minutes;

    public static readonly BindableProperty SelectedTimeProperty = BindableProperty.Create(
        nameof(SelectedTime),
        typeof(int),
        typeof(HourMinutePicker),
        0,
        BindingMode.TwoWay,
        propertyChanged: OnSelectedTimeChanged);

    public int SelectedTime
    {
        get => (int)GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    public HourMinutePicker()
    {
        _hours = new ObservableCollection<object>(
            Enumerable.Range(0, 24).Select(i => (object)$"{i:D2}"));
        _minutes = new ObservableCollection<object>(
            Enumerable.Range(0, 60).Select(i => (object)$"{i:D2}"));

        _hourColumn = new PickerColumn { HeaderText = "Hour", ItemsSource = _hours, SelectedIndex = 0 };
        _minuteColumn = new PickerColumn { HeaderText = "Minute", ItemsSource = _minutes, SelectedIndex = 0 };

        // Customize ColumnHeaderView — triggers internal theme registrations in ThemeElement.elements
        ColumnHeaderView = new PickerColumnHeaderView
        {
            Height = 40,
            TextStyle = new PickerTextStyle { FontSize = 16, TextColor = Colors.DarkSlateBlue }
        };

        // Customize SelectionView — triggers additional ThemeElement.elements registrations
        SelectionView = new PickerSelectionView
        {
            Background = Colors.LightBlue,
            CornerRadius = 8
        };

        Mode = PickerMode.Dialog;

        Columns.Add(_hourColumn);
        Columns.Add(_minuteColumn);

        Console.WriteLine($"[HourMinutePicker] Constructed (0x{GetHashCode():X8}), elements={ThemeElementInspector.GetElementsCount()}");
    }

    private static void OnSelectedTimeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is HourMinutePicker picker && newValue is int totalMinutes)
        {
            picker._hourColumn.SelectedIndex = Math.Clamp(totalMinutes / 60, 0, 23);
            picker._minuteColumn.SelectedIndex = Math.Clamp(totalMinutes % 60, 0, 59);
        }
    }

    public int GetSelectedHour() => _hourColumn.SelectedIndex;
    public int GetSelectedMinute() => _minuteColumn.SelectedIndex;
}
