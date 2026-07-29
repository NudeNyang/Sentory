using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class ExplorerImageDragSessionTests
{
    [Fact]
    public void PublishedSelectionCanBeSharedWithinMaximumAge()
    {
        var session = new ExplorerImageDragSession(
            TimeSpan.FromMilliseconds(500));
        var observedAt = DateTimeOffset.UtcNow;

        var generation = session.Publish(
            (40, 80),
            ["photo.png"],
            observedAt);

        var found = session.TryGet(
            observedAt.AddMilliseconds(200),
            out var sharedGeneration,
            out var start,
            out var paths);

        Assert.True(found);
        Assert.Equal(generation, sharedGeneration);
        Assert.Equal((40, 80), start);
        Assert.Equal(["photo.png"], paths);
    }

    [Fact]
    public void ExpiredSelectionIsNotShared()
    {
        var session = new ExplorerImageDragSession(
            TimeSpan.FromMilliseconds(500));
        var observedAt = DateTimeOffset.UtcNow;
        session.Publish((40, 80), ["photo.png"], observedAt);

        var found = session.TryGet(
            observedAt.AddMilliseconds(501),
            out _,
            out _,
            out _);

        Assert.False(found);
        Assert.False(session.TryGetRecent(
            observedAt.AddMilliseconds(501),
            out _,
            out _,
            out _));
    }

    [Fact]
    public void EndedSelectionCanOnlyBeRecoveredAsRecent()
    {
        var session = new ExplorerImageDragSession(
            TimeSpan.FromMilliseconds(500));
        var observedAt = DateTimeOffset.UtcNow;
        var generation = session.Publish(
            (40, 80),
            ["photo.png"],
            observedAt);

        session.End(generation);

        Assert.False(session.TryGet(observedAt, out _, out _, out _));
        Assert.True(session.TryGetRecent(
            observedAt.AddMilliseconds(200),
            out var recentGeneration,
            out var start,
            out var paths));
        Assert.Equal(generation, recentGeneration);
        Assert.Equal((40, 80), start);
        Assert.Equal(["photo.png"], paths);
    }

    [Fact]
    public void EndForOlderGenerationDoesNotEndNewerSelection()
    {
        var session = new ExplorerImageDragSession(
            TimeSpan.FromMilliseconds(500));
        var observedAt = DateTimeOffset.UtcNow;
        var firstGeneration = session.Publish(
            (40, 80),
            ["first.png"],
            observedAt);
        var secondGeneration = session.Publish(
            (50, 90),
            ["second.png"],
            observedAt.AddMilliseconds(10));

        session.End(firstGeneration);

        Assert.True(session.TryGet(
            observedAt.AddMilliseconds(20),
            out var activeGeneration,
            out _,
            out var paths));
        Assert.Equal(secondGeneration, activeGeneration);
        Assert.Equal(["second.png"], paths);
    }

    [Fact]
    public void ClearedSelectionCannotBeRecovered()
    {
        var session = new ExplorerImageDragSession(
            TimeSpan.FromMilliseconds(500));
        var observedAt = DateTimeOffset.UtcNow;
        session.Publish((40, 80), ["photo.png"], observedAt);

        session.Clear();

        Assert.False(session.TryGet(observedAt, out _, out _, out _));
        Assert.False(session.TryGetRecent(
            observedAt,
            out _,
            out _,
            out _));
    }
}
