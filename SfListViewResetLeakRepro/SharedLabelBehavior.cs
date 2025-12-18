using Microsoft.Maui.Controls;

namespace SfListViewResetLeakRepro;

public sealed class SharedLabelBehavior : Behavior<Label>
{
	public static SharedLabelBehavior Instance { get; } = new();

	protected override void OnAttachedTo(Label bindable)
		=> base.OnAttachedTo(bindable);

	protected override void OnDetachingFrom(Label bindable)
		=> base.OnDetachingFrom(bindable);
}
