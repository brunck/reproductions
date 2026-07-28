namespace BindablePropertyConcurrencyRepro;

/// <summary>
/// A <see cref="Label"/> carrying two custom bindable properties that no platform property
/// mapper knows about.
///
/// This matters for the isolation. Writing a *mapped* property (say <c>CharacterSpacing</c>)
/// off the UI thread makes <c>Element.UpdateHandlerValue</c> reach through to the platform view,
/// and Android's own <c>ViewRootImpl.checkThread</c> throws on the very first iteration — which
/// masks the framework defect entirely.
///
/// Writing these two instead means <c>UpdateHandlerValue</c> resolves to nothing: no platform
/// view is touched, on any thread. Everything that executes is platform-agnostic
/// <c>Controls</c> code, so the only shared mutable state in play is
/// <c>Element._pendingHandlerUpdatesFromBPSet</c>.
/// </summary>
public class RaceLabel : Label
{
	public static readonly BindableProperty RaceAlphaProperty =
		BindableProperty.Create(nameof(RaceAlpha), typeof(int), typeof(RaceLabel), 0);

	public static readonly BindableProperty RaceBetaProperty =
		BindableProperty.Create(nameof(RaceBeta), typeof(int), typeof(RaceLabel), 0);

	public int RaceAlpha
	{
		get => (int)GetValue(RaceAlphaProperty);
		set => SetValue(RaceAlphaProperty, value);
	}

	public int RaceBeta
	{
		get => (int)GetValue(RaceBetaProperty);
		set => SetValue(RaceBetaProperty, value);
	}
}
