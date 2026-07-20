using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class ExplorerFileDragActivationStateTests
{
    [Fact]
    public void ArmsFastDragFromLastPointerUpPosition()
    {
        var state = new ExplorerFileDragActivationState();

        Assert.False(state.Observe(false, (20, 30), () => true));
        Assert.True(state.Observe(true, (120, 130), () => true));
    }

    [Fact]
    public void WaitsForMinimumDistanceDuringOrdinaryDrag()
    {
        var state = new ExplorerFileDragActivationState();

        Assert.False(state.Observe(false, (20, 30), () => true));
        Assert.False(state.Observe(true, (23, 34), () => true));
        Assert.True(state.Observe(true, (29, 30), () => false));
    }

    [Fact]
    public void RejectsPointerDownWhenForegroundIsNotExplorer()
    {
        var state = new ExplorerFileDragActivationState();

        Assert.False(state.Observe(false, (20, 30), () => true));
        Assert.False(state.Observe(true, (120, 130), () => false));
        Assert.False(state.Observe(true, (220, 230), () => true));
    }

    [Fact]
    public void ResetAllowsNextExplorerDragToBeEvaluated()
    {
        var state = new ExplorerFileDragActivationState();

        Assert.False(state.Observe(false, (20, 30), () => true));
        Assert.True(state.Observe(true, (120, 130), () => true));

        state.ResetActiveDrag();

        Assert.True(state.Observe(true, (220, 230), () => true));
    }
}
