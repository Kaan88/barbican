// Mirrors: Rust listsync.rs set_vec (replace the whole model with one notification). Owner mailcore.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Barbican.Presentation.Common;

/// <summary>An observable collection that can be replaced as a whole with a single Reset notification.</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public BulkObservableCollection()
    {
    }

    public BulkObservableCollection(IEnumerable<T> rows)
        : base(rows)
    {
    }

    /// <summary>Replaces the content; the view is told once (Reset), not once per row.</summary>
    public void ReplaceAll(IEnumerable<T> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        CheckReentrancy();
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(row);
        }

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
