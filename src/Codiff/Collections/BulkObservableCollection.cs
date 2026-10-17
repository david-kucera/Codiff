using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Codiff.Collections;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> whose whole content can be replaced with a single
/// <see cref="NotifyCollectionChangedAction.Reset"/> notification, instead of one
/// notification per removed and added item.
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Replaces all items and raises one <see cref="NotifyCollectionChangedAction.Reset"/> notification.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();

        // Materialize lazy sequences first: they might enumerate this very collection.
        if (items is not ICollection<T>)
            items = items.ToList();

        if (Items is List<T> list)
        {
            list.Clear();
            list.AddRange(items);
        }
        else
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
