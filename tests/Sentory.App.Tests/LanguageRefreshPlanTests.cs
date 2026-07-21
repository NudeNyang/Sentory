namespace Sentory.App.Tests;

public sealed class LanguageRefreshPlanTests
{
    [Fact]
    public void PlacesVisibleItemsInFirstBatchAndBatchesTheRemainder()
    {
        var allItems = Enumerable.Range(0, 100).ToArray();
        int[] visibleItems = [42, 43, 44, 45];

        var batches = LanguageRefreshPlan.Create(
            allItems,
            visibleItems,
            backgroundBatchSize: 20);

        Assert.Equal(visibleItems, batches[0]);
        Assert.All(batches.Skip(1), batch =>
            Assert.InRange(batch.Count, 1, 20));
        Assert.Equal(
            allItems,
            batches.SelectMany(batch => batch).Order().ToArray());
    }
}
