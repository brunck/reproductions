namespace BindablePropertyConcurrencyRepro;

/// <summary>
/// A custom control whose bindable-property callback writes a *second* bindable property on
/// itself. This reproduces the shape of the third-party frame in our production stack
/// (<c>Skeleton.HandleIsBusyChanged</c> → <c>SetBackgroundColor</c>): the inner
/// <c>SetValue</c> re-enters <c>Element.OnBindablePropertySet</c> while the outer one is still
/// between its <c>Add</c> and <c>Remove</c>, so the shared set is nested with two different
/// property names at once.
/// </summary>
public class RaceControl : ContentView
{
	public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
		nameof(IsBusy),
		typeof(bool),
		typeof(RaceControl),
		false,
		propertyChanged: HandleIsBusyChanged);

	public bool IsBusy
	{
		get => (bool)GetValue(IsBusyProperty);
		set => SetValue(IsBusyProperty, value);
	}

	static void HandleIsBusyChanged(BindableObject bindable, object oldValue, object newValue)
	{
		var control = (RaceControl)bindable;

		// Writes another bindable property on the same element from inside the callback.
		control.BackgroundColor = (bool)newValue ? Colors.DarkOrange : Colors.Transparent;
	}
}
