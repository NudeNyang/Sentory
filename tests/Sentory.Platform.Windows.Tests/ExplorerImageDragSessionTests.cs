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

        session.Publish((40, 80), ["photo.png"], observedAt);

        var found = session.TryGet(
            observedAt.AddMilliseconds(200),
            out var start,
            out var paths);

        Assert.True(found);
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
            out _);

        Assert.False(found);
    }

    [Fact]
    public void ClearedSelectionIsNotShared()
    {
        var session = new ExplorerImageDragSession(
            TimeSpan.FromMilliseconds(500));
        var observedAt = DateTimeOffset.UtcNow;
        session.Publish((40, 80), ["photo.png"], observedAt);

        session.Clear();

        Assert.False(session.TryGet(observedAt, out _, out _));
    }
}
