using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Sentory.App;

internal sealed class ResettableObservableCollection<T> :
    ObservableCollection<T>
{
    private const int ResetChangeThreshold = 32;

    public void ReconcileAll(
        IEnumerable<T> items,
        IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        comparer ??= EqualityComparer<T>.Default;
        var replacement = items as IReadOnlyList<T> ?? items.ToArray();
        if (EstimateChangeCount(replacement, comparer) >=
            ResetChangeThreshold)
        {
            ReplaceAll(replacement);
            return;
        }

        var unmatched = replacement.ToList();

        for (var currentIndex = Count - 1;
             currentIndex >= 0;
             currentIndex--)
        {
            var replacementIndex = unmatched.FindIndex(item =>
                comparer.Equals(this[currentIndex], item));
            if (replacementIndex >= 0)
            {
                unmatched.RemoveAt(replacementIndex);
            }
            else
            {
                RemoveAt(currentIndex);
            }
        }

        for (var targetIndex = 0;
             targetIndex < replacement.Count;
             targetIndex++)
        {
            var desiredItem = replacement[targetIndex];
            if (targetIndex < Count &&
                comparer.Equals(this[targetIndex], desiredItem))
            {
                continue;
            }

            var currentIndex = IndexOf(
                desiredItem,
                targetIndex + 1,
                comparer);
            if (currentIndex >= 0)
            {
                Move(currentIndex, targetIndex);
            }
            else
            {
                Insert(targetIndex, desiredItem);
            }
        }
    }

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

    private int IndexOf(
        T item,
        int startIndex,
        IEqualityComparer<T> comparer)
    {
        for (var index = startIndex; index < Count; index++)
        {
            if (comparer.Equals(this[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private int EstimateChangeCount(
        IReadOnlyList<T> replacement,
        IEqualityComparer<T> comparer)
    {
        var working = Items.ToList();
        var unmatched = replacement.ToList();
        var changes = 0;
        for (var currentIndex = working.Count - 1;
             currentIndex >= 0;
             currentIndex--)
        {
            var replacementIndex = unmatched.FindIndex(item =>
                comparer.Equals(working[currentIndex], item));
            if (replacementIndex >= 0)
            {
                unmatched.RemoveAt(replacementIndex);
            }
            else
            {
                working.RemoveAt(currentIndex);
                changes++;
            }
        }

        for (var targetIndex = 0;
             targetIndex < replacement.Count;
             targetIndex++)
        {
            var desiredItem = replacement[targetIndex];
            if (targetIndex < working.Count &&
                comparer.Equals(working[targetIndex], desiredItem))
            {
                continue;
            }

            var currentIndex = -1;
            for (var index = targetIndex + 1;
                 index < working.Count;
                 index++)
            {
                if (comparer.Equals(working[index], desiredItem))
                {
                    currentIndex = index;
                    break;
                }
            }
            if (currentIndex >= 0)
            {
                working.RemoveAt(currentIndex);
                working.Insert(targetIndex, desiredItem);
            }
            else
            {
                working.Insert(targetIndex, desiredItem);
            }

            changes++;
            if (changes >= ResetChangeThreshold)
            {
                return changes;
            }
        }

        return changes;
    }
}
