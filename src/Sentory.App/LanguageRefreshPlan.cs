namespace Sentory.App;

internal static class LanguageRefreshPlan
{
    public static IReadOnlyList<IReadOnlyList<T>> Create<T>(
        IReadOnlyList<T> allItems,
        IReadOnlyList<T> visibleItems,
        int backgroundBatchSize,
        IEqualityComparer<T>? comparer = null)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(allItems);
        ArgumentNullException.ThrowIfNull(visibleItems);
        if (backgroundBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backgroundBatchSize));
        }

        comparer ??= EqualityComparer<T>.Default;
        var available = new HashSet<T>(allItems, comparer);
        var scheduled = new HashSet<T>(comparer);
        var firstBatch = visibleItems
            .Where(available.Contains)
            .Where(scheduled.Add)
            .ToArray();
        var remaining = allItems
            .Where(scheduled.Add)
            .ToArray();
        var batches = new List<IReadOnlyList<T>>();
        if (firstBatch.Length > 0)
        {
            batches.Add(firstBatch);
        }

        for (var start = 0; start < remaining.Length;
             start += backgroundBatchSize)
        {
            batches.Add(remaining
                .Skip(start)
                .Take(backgroundBatchSize)
                .ToArray());
        }

        return batches;
    }
}
