using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class WeChatPassiveDropStateTests
{
    private static readonly WeChatDropTarget Target = new(
        new nint(20),
        84,
        new WindowBounds(300, 120, 940, 820));

    [Fact]
    public void KeepsRecentTargetAcrossSingleReleaseFrameGap()
    {
        var state = new WeChatPassiveDropState();
        state.Begin((100, 100), ["photo.png"]);
        state.Observe((450, 300), Target);

        state.Observe((450, 300), null);

        Assert.True(state.TryTakeCompleted(out var target, out var paths));
        Assert.Equal(Target, target);
        Assert.Equal(["photo.png"], paths);
    }

    [Fact]
    public void KeepsTargetWhileWeChatPreviewCoversDropArea()
    {
        var state = new WeChatPassiveDropState();
        state.Begin((100, 100), ["photo.png"]);
        state.Observe((450, 300), Target);

        for (var frame = 0; frame < 120; frame++)
        {
            state.Observe((450, 300), null);
        }

        Assert.True(state.TryTakeCompleted(out var target, out var paths));
        Assert.Equal(Target, target);
        Assert.Equal(["photo.png"], paths);
    }

    [Fact]
    public void ClearsRecentTargetAfterPointerLeavesWeChatBounds()
    {
        var state = new WeChatPassiveDropState();
        state.Begin((100, 100), ["photo.png"]);
        state.Observe((450, 300), Target);

        state.Observe((100, 100), null);

        Assert.False(state.TryTakeCompleted(out _, out _));
    }

    [Fact]
    public void KeepsTrackingDuringReleaseGraceUntilTargetAppears()
    {
        var state = new WeChatPassiveDropState();
        state.Begin((100, 100), ["photo.png"]);
        state.Observe((450, 300), null);

        Assert.False(state.TryTakeCompleted(out _, out _));

        state.Observe((450, 300), Target);

        Assert.True(state.TryTakeCompleted(out _, out var paths));
        Assert.Equal(["photo.png"], paths);
    }
}
