using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class WhatsAppPassiveDropStateTests
{
    private static readonly WhatsAppDropTarget Target = new(
        new nint(20),
        new nint(21),
        84,
        new WindowBounds(0, 0, 900, 700));

    [Fact]
    public void CompletesExplorerImageDragWithoutOwningOleDrop()
    {
        var state = new WhatsAppPassiveDropState();

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
        var state = new WhatsAppPassiveDropState();

        state.Begin((10, 10), ["one.png"]);
        state.Observe((14, 14), Target);

        Assert.False(state.TryTakeCompleted(out _, out _));
    }

    [Fact]
    public void DoesNotCompleteAfterPointerLeavesWhatsApp()
    {
        var state = new WhatsAppPassiveDropState();

        state.Begin((10, 10), ["one.png"]);
        state.Observe((40, 40), Target);
        state.Observe((50, 50), null);

        Assert.False(state.TryTakeCompleted(out _, out _));
    }

    [Fact]
    public void CancellationDiscardsObservedSelection()
    {
        var state = new WhatsAppPassiveDropState();

        state.Begin((10, 10), ["one.png"]);
        state.Observe((40, 40), Target);
        state.Cancel();

        Assert.False(state.TryTakeCompleted(out _, out _));
    }
}
