using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class KakaoDropPassThroughStateTests
{
    [Fact]
    public void ReleasesObservedFilesOnlyAfterNativeDragEnds()
    {
        var state = new KakaoDropPassThroughState();
        var target = new KakaoDropTarget(
            new nint(10),
            new nint(11),
            42,
            new WindowBounds(0, 0, 500, 700),
            new WindowBounds(0, 600, 500, 700));

        state.Observe(target, ["one.png", "two.png"]);

        Assert.True(state.IsActive);
        Assert.False(state.TryTakeCompleted(
            leftMouseButtonDown: true,
            out _,
            out _));
        Assert.True(state.TryTakeCompleted(
            leftMouseButtonDown: false,
            out var completedTarget,
            out var paths));
        Assert.Equal(target, completedTarget);
        Assert.Equal(["one.png", "two.png"], paths);
        Assert.False(state.IsActive);
        Assert.False(state.TryTakeCompleted(
            leftMouseButtonDown: false,
            out _,
            out _));
    }

    [Fact]
    public void EscapeCancellationDiscardsObservedFiles()
    {
        var state = new KakaoDropPassThroughState();
        state.Observe(
            new KakaoDropTarget(
                new nint(10),
                new nint(11),
                42,
                new WindowBounds(0, 0, 500, 700),
                new WindowBounds(0, 600, 500, 700)),
            ["one.png"]);

        state.Cancel();

        Assert.False(state.IsActive);
        Assert.False(state.TryTakeCompleted(
            leftMouseButtonDown: false,
            out _,
            out _));
    }
}
