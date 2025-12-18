using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SfListViewResetLeakRepro;

public sealed class MainViewModel : INotifyPropertyChanged
{
	private static readonly bool ForceGcAfterEachRefresh = true;

	// Use larger batch (tested with 20) and no other changes to demonstrate the leak disappears with larger numbers.
	private const int ItemsPerRefresh = 1;

	private bool _isRefreshing;
	private int _refreshCount;
    private ObservableCollection<RowItem> _items = [];

	public ObservableCollection<RowItem> Items
	{
		get => _items;
		private set
		{
			if (ReferenceEquals(_items, value))
			{
				return;
			}

			_items = value;
			OnPropertyChanged();
		}
	}

	public ICommand RefreshCommand { get; }
	public ICommand RunGcCommand { get; }

	public bool IsRefreshing
	{
		get => _isRefreshing;
		set
		{
			if (_isRefreshing == value)
            {
                return;
            }

            _isRefreshing = value;
			OnPropertyChanged();
		}
	}

	public int RefreshCount
	{
		get => _refreshCount;
		private set
		{
			if (_refreshCount == value)
            {
                return;
            }

            _refreshCount = value;
			OnPropertyChanged();
		}
	}

	public MainViewModel()
	{
        Items = [];
		foreach (var item in CreateItems(refreshCount: 0))
        {
            Items.Add(item);
        }

        RefreshCommand = new Command(DoRefresh);
		RunGcCommand = new Command(OnShowLeakInfo);
	}

	private void DoRefresh()
	{
		try
		{
			IsRefreshing = true;
			RefreshCount++;

			var nextItems = CreateItems(refreshCount: RefreshCount);

			Items.Clear();

			// WORKAROUND (uncomment to avoid Reset and the leak signature):
			// 1) Comment out the line above (Items.Clear), then uncomment this block.
			// 2) Re-run the same refresh + gcdump steps AND with ItemsPerRefresh = 1; TemplateViewCache should remain stable.
			//
            //if (Items.Count > 0)
            //{
            //    for (var i = 0; i < Items.Count; i++)
            //    {
            //        Items.RemoveAt(i);
            //    }
            //}

			foreach (var item in nextItems)
            {
				Items.Add(item);
            }

            if (ForceGcAfterEachRefresh)
			{
				try
				{
					GC.Collect();
					GC.WaitForPendingFinalizers();
					GC.Collect();
					Console.WriteLine($"Diagnostic: forced GC after refresh #{RefreshCount}");
				}
				catch
				{
					// best-effort
				}
			}

			
		}
		finally
		{
			IsRefreshing = false;
		}
	}

	private void OnShowLeakInfo()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

        var managedBytes = GC.GetTotalMemory(forceFullCollection: false);

		var page = Application.Current?.Windows.FirstOrDefault()?.Page;
		page?.DisplayAlert(
			"GC",
			$"GC run.\n\nRefreshes: {RefreshCount}\nItems per refresh: {ItemsPerRefresh}\n\nCurrent Items: {Items.Count}\n\nManaged bytes: {managedBytes:n0}",
			"OK");
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

	private static List<RowItem> CreateItems(int refreshCount)
	{
		var items = new List<RowItem>(capacity: ItemsPerRefresh);
		for (var index = 0; index < ItemsPerRefresh; index++)
		{
			var item = new RowItem
			{
				Text = $"Item {index + 1} (refresh {refreshCount})",
				UseAlternateTemplate = false, // ((refreshCount + index) % 2 == 0),
				DetailsVisible = false //((refreshCount + index) % 3 == 0)
			};

			//item.Details.Add("Detail A");
			//item.Details.Add("Detail B");

			items.Add(item);
		}

		return items;
	}
}
