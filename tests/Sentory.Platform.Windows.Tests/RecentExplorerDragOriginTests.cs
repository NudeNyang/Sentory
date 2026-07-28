using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class RecentExplorerDragOriginTests
{
    [Fact]
    public void RecoversFastDragAfterPointerMovedFarFromExplorer()
    {
        var state = new RecentExplorerDragOrigin(
            TimeSpan.FromMilliseconds(150));
        var observedAt = DateTimeOffset.UtcNow;
        state.Observe((100, 100), new nint(20), observedAt);

        var found = state.TryGet(
            observedAt.AddMilliseconds(32),
            out var explorer,
            out var start);

        Assert.True(found);
        Assert.Equal(new nint(20), explorer);
        Assert.Equal((100, 100), start);
    }

    [Fact]
    public void RejectsStaleExplorerOrigin()
    {
        var state = new RecentExplorerDragOrigin(
            TimeSpan.FromMilliseconds(150));
        var observedAt = DateTimeOffset.UtcNow;
        state.Observe((100, 100), new nint(20), observedAt);

        Assert.False(state.TryGet(
            observedAt.AddMilliseconds(151),
            out _,
            out _));
    }

    [Fact]
    public void IgnoresNonExplorerPointerUpSamples()
    {
        var state = new RecentExplorerDragOrigin(
            TimeSpan.FromMilliseconds(150));

        state.Observe(
            (100, 100),
            nint.Zero,
            DateTimeOffset.UtcNow);

        Assert.False(state.TryGet(
            DateTimeOffset.UtcNow,
            out _,
            out _));
    }
}
