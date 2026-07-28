using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class LinePassiveDropStateTests
{
    private static readonly LineDropTarget Target = new(
        new nint(20),
        84,
        new WindowBounds(0, 0, 900, 700));

    [Fact]
    public void CompletesExplorerImageDragOverLine()
    {
        var state = new LinePassiveDropState();

        state.Begin((10, 10), ["one.png", "two.png"]);
        state.Observe((40, 40), Target);

        Assert.True(state.TryTakeCompleted(
            out var completedTarget,
            out var paths));
        Assert.Equal(Target, completedTarget);
        Assert.Equal(["one.png", "two.png"], paths);
    }

    [Fact]
    public void DoesNotCompleteClickOrDropOutsideLine()
    {
        var click = new LinePassiveDropState();
        click.Begin((10, 10), ["one.png"]);
        click.Observe((14, 14), Target);
        Assert.False(click.TryTakeCompleted(out _, out _));

        var outside = new LinePassiveDropState();
        outside.Begin((10, 10), ["one.png"]);
        outside.Observe((40, 40), Target);
        outside.Observe((50, 50), null);
        Assert.False(outside.TryTakeCompleted(out _, out _));
    }

    [Fact]
    public void KeepsTrackingDuringReleaseGraceUntilTargetAppears()
    {
        var state = new LinePassiveDropState();
        state.Begin((10, 10), ["one.png"]);
        state.Observe((40, 40), null);

        Assert.False(state.TryTakeCompleted(out _, out _));

        state.Observe((40, 40), Target);

        Assert.True(state.TryTakeCompleted(out _, out var paths));
        Assert.Equal(["one.png"], paths);
    }
}
