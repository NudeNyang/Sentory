using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Sentory.App;

internal sealed class ResettableObservableCollection<T> :
    ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var replacement = items as IReadOnlyList<T> ?? items.ToArray();

        CheckReentrancy();
        Items.Clear();
        for (var index = 0; index < replacement.Count; index++)
        {
            Items.Add(replacement[index]);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}
