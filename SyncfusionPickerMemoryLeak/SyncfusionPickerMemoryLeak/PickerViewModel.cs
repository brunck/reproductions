using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SyncfusionPickerMemoryLeak;

/// <summary>
/// Minimal ViewModel mirroring production's TimerSettings pattern.
/// Holds selected time values and display strings updated from picker events.
/// </summary>
public class PickerViewModel : INotifyPropertyChanged
{
    private int _selectedOverrideTime;
    private int _selectedServiceTime;
    private string _selectedOverrideString = "00:00";
    private string _selectedServiceString = "00:00";

    public int SelectedOverrideTime
    {
        get => _selectedOverrideTime;
        set { if (_selectedOverrideTime != value) { _selectedOverrideTime = value; OnPropertyChanged(); } }
    }

    public int SelectedServiceTime
    {
        get => _selectedServiceTime;
        set { if (_selectedServiceTime != value) { _selectedServiceTime = value; OnPropertyChanged(); } }
    }

    public string SelectedOverrideString
    {
        get => _selectedOverrideString;
        set { if (_selectedOverrideString != value) { _selectedOverrideString = value; OnPropertyChanged(); } }
    }

    public string SelectedServiceString
    {
        get => _selectedServiceString;
        set { if (_selectedServiceString != value) { _selectedServiceString = value; OnPropertyChanged(); } }
    }

    public void UpdateOverrideTimeDisplay(int hour, int minute)
    {
        SelectedOverrideTime = hour * 60 + minute;
        SelectedOverrideString = $"{hour:D2}:{minute:D2}";
    }

    public void UpdateServiceTimeDisplay(int hour, int minute)
    {
        SelectedServiceTime = hour * 60 + minute;
        SelectedServiceString = $"{hour:D2}:{minute:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
