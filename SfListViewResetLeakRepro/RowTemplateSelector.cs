using Microsoft.Maui.Controls;

namespace SfListViewResetLeakRepro;

public sealed class RowTemplateSelector : DataTemplateSelector
{
	public DataTemplate? PrimaryTemplate { get; set; }
	public DataTemplate? AlternateTemplate { get; set; }

	private static readonly DataTemplate FallbackTemplate = new(() => new ContentView());

	protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
	{
		if (item is not RowItem model)
			return PrimaryTemplate ?? AlternateTemplate ?? FallbackTemplate;

		if (model.UseAlternateTemplate)
			return AlternateTemplate ?? PrimaryTemplate ?? FallbackTemplate;

		return PrimaryTemplate ?? AlternateTemplate ?? FallbackTemplate;
	}
}
