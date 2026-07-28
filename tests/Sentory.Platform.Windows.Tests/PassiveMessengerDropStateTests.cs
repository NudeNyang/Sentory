using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class PassiveMessengerDropStateTests
{
    private static readonly TargetWindow Target = new(
        new WindowBounds(0, 0, 900, 700));

    [Fact]
    public void CompletesExplorerImageDragWithoutOwningOleDrop()
    {
        var state = CreateState();

        state.Begin((10, 10), ["one.png", "two.png"]);
        state.Observe((40, 40), Target);

        Assert.True(state.TryTakeCompleted(
            out var completedTarget,
            out var paths));
        Assert.Equal(Target, completedTarget);
        Assert.Equal(["one.png", "two.png"], paths);
    }

    [Fact]
    public void DoesNotCompleteClickThatNeverBecameDrag()
    {
        var state = CreateState();

        state.Begin((10, 10), ["one.png"]);
        state.Observe((14, 14), Target);

        Assert.False(state.TryTakeCompleted(out _, out _));
    }

    [Fact]
    public void DoesNotCompleteAfterPointerLeavesTarget()
    {
        var state = CreateState();

        state.Begin((10, 10), ["one.png"]);
        state.Observe((40, 40), Target);
        state.Observe((950, 750), null);

        Assert.False(state.TryTakeCompleted(out _, out _));
    }

    [Fact]
    public void KeepsTargetAcrossTransientNativePreviewSurface()
    {
        var state = CreateState();

        state.Begin((10, 10), ["one.png"]);
        state.Observe((40, 40), Target);
        state.Observe((50, 50), null);

        Assert.True(state.TryTakeCompleted(out _, out _));
    }

    [Fact]
    public void CancellationDiscardsObservedSelection()
    {
        var state = CreateState();

        state.Begin((10, 10), ["one.png"]);
        state.Observe((40, 40), Target);
        state.Reset();

        Assert.False(state.TryTakeCompleted(out _, out _));
    }

    private static PassiveMessengerDropState<TargetWindow> CreateState() =>
        new(target => target.Bounds);

    private sealed record TargetWindow(WindowBounds Bounds);
}
