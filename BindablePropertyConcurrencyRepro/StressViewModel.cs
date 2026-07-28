using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BindablePropertyConcurrencyRepro;

/// <summary>
/// A plain INotifyPropertyChanged view model — no <c>ObservableObject</c> base class, no
/// marshalling of its own. Scenarios B, C and D raise its notifications from a background
/// thread, which is the shape our production code had.
/// </summary>
public sealed class StressViewModel : INotifyPropertyChanged
{
	string _text = "0";
	bool _isBusy;

	public event PropertyChangedEventHandler? PropertyChanged;

	public string Text
	{
		get => _text;
		set => Set(ref _text, value);
	}

	public bool IsBusy
	{
		get => _isBusy;
		set => Set(ref _isBusy, value);
	}

	void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
			return;

		field = value;

		// Raised on whatever thread the setter was called from — deliberately unmarshalled.
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
