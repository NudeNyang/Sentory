using System.Collections.Specialized;

namespace Sentory.App.Tests;

public sealed class ResettableObservableCollectionTests
{
    [Fact]
    public void ReconcileAllRemovesOnlyItemsOutsideTheFilteredResult()
    {
        var collection = new ResettableObservableCollection<string>
        {
            "link",
            "image-1",
            "image-2",
            "other-link"
        };
        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, eventArgs) =>
            actions.Add(eventArgs.Action);

        collection.ReconcileAll(["image-1", "image-2"]);

        Assert.Equal(["image-1", "image-2"], collection);
        Assert.Equal(
            [
                NotifyCollectionChangedAction.Remove,
                NotifyCollectionChangedAction.Remove
            ],
            actions);
        Assert.DoesNotContain(
            NotifyCollectionChangedAction.Reset,
            actions);
    }

    [Fact]
    public void ReconcileAllMovesExistingItemsWhenOnlySortOrderChanges()
    {
        var collection = new ResettableObservableCollection<string>
        {
            "newest",
            "middle",
            "oldest"
        };
        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, eventArgs) =>
            actions.Add(eventArgs.Action);

        collection.ReconcileAll(["oldest", "middle", "newest"]);

        Assert.Equal(["oldest", "middle", "newest"], collection);
        Assert.All(
            actions,
            action => Assert.Equal(
                NotifyCollectionChangedAction.Move,
                action));
        Assert.DoesNotContain(
            NotifyCollectionChangedAction.Reset,
            actions);
    }
}
