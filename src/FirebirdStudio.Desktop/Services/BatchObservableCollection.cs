using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FirebirdStudio.Desktop.Services;

/// <summary>
/// Observable collection that appends a group of items with one collection-change
/// notification, avoiding one TreeView update for every metadata row.
/// </summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IReadOnlyCollection<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            return;

        CheckReentrancy();
        var startIndex = Count;
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, (IList)items.ToList(), startIndex));
    }
}
