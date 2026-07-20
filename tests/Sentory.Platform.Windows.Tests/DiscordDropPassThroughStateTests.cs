using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordDropPassThroughStateTests
{
    [Fact]
    public void ReleasesObservedFilesOnlyAfterNativeDragEnds()
    {
        var state = new DiscordDropPassThroughState();
        var target = new DiscordDropTarget(
            new nint(20),
            new nint(21),
            84,
            new WindowBounds(0, 0, 900, 700));

        state.Observe(target, ["one.png", "two.png"]);

        Assert.True(state.IsActive);
        Assert.False(state.TryTakeCompleted(true, out _, out _));
        Assert.True(state.TryTakeCompleted(
            false,
            out var completedTarget,
            out var paths));
        Assert.Equal(target, completedTarget);
        Assert.Equal(["one.png", "two.png"], paths);
        Assert.False(state.IsActive);
    }

    [Fact]
    public void EscapeCancellationDiscardsObservedFiles()
    {
        var state = new DiscordDropPassThroughState();
        state.Observe(
            new DiscordDropTarget(
                new nint(20),
                new nint(21),
                84,
                new WindowBounds(0, 0, 900, 700)),
            ["one.png"]);

        state.Cancel();

        Assert.False(state.IsActive);
        Assert.False(state.TryTakeCompleted(false, out _, out _));
    }
}
