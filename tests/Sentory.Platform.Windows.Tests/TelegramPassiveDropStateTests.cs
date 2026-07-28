using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class TelegramPassiveDropStateTests
{
    private static readonly TelegramDropTarget Target = new(
        new nint(20),
        84,
        new WindowBounds(300, 120, 940, 820));

    [Fact]
    public void KeepsRecentTargetAcrossSingleReleaseFrameGap()
    {
        var state = new TelegramPassiveDropState();
        state.Begin((100, 100), ["photo.png"]);
        state.Observe((450, 300), Target);

        state.Observe((450, 300), null);

        Assert.True(state.TryTakeCompleted(out var target, out var paths));
        Assert.Equal(Target, target);
        Assert.Equal(["photo.png"], paths);
    }

    [Fact]
    public void ClearsRecentTargetAfterPointerLeavesTelegramBounds()
    {
        var state = new TelegramPassiveDropState();
        state.Begin((100, 100), ["photo.png"]);
        state.Observe((450, 300), Target);

        state.Observe((100, 100), null);

        Assert.False(state.TryTakeCompleted(out _, out _));
    }

    [Fact]
    public void ClearsRecentTargetAfterSeveralUnmatchedFrames()
    {
        var state = new TelegramPassiveDropState();
        state.Begin((100, 100), ["photo.png"]);
        state.Observe((450, 300), Target);

        for (var frame = 0; frame < 4; frame++)
        {
            state.Observe((450, 300), null);
        }

        Assert.False(state.TryTakeCompleted(out _, out _));
    }
}
