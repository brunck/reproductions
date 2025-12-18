namespace SfListViewResetLeakRepro;

using System.Collections.ObjectModel;

public sealed class RowItem
{
	public string Text { get; set; } = string.Empty;

	// Used by the DataTemplateSelector to choose between two templates.
	public bool UseAlternateTemplate { get; set; }

	public bool DetailsVisible { get; set; }

	public ObservableCollection<string> Details { get; } = [];
}
