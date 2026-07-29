using Sentory.Platform.Windows.Runtime;
using Sentory.Platform.Windows.Interop;

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

    [Fact]
    public void LowLevelExplorerPointerDownSurvivesDelayedUiPoll()
    {
        var state = new RecentExplorerDragOrigin(TimeSpan.FromSeconds(2));
        var observedAt = DateTimeOffset.UtcNow;
        var native = new FakeNative();

        ExplorerPointerDownOriginTracker.Observe(
            state,
            native,
            new PointerTrigger(
                Guid.NewGuid(),
                native.Explorer,
                native.ProcessId,
                100,
                100,
                observedAt,
                false));

        Assert.True(state.TryGet(
            observedAt.AddMilliseconds(600),
            out var explorer,
            out var start));
        Assert.Equal(native.Explorer, explorer);
        Assert.Equal((100, 100), start);
    }

    [Fact]
    public void NonExplorerPointerDownClearsOlderExplorerOrigin()
    {
        var state = new RecentExplorerDragOrigin(TimeSpan.FromSeconds(2));
        var observedAt = DateTimeOffset.UtcNow;
        var native = new FakeNative();
        state.Observe((100, 100), native.Explorer, observedAt);
        native.ProcessName = "chrome";

        ExplorerPointerDownOriginTracker.Observe(
            state,
            native,
            new PointerTrigger(
                Guid.NewGuid(),
                native.Explorer,
                native.ProcessId,
                300,
                300,
                observedAt.AddMilliseconds(100),
                false));

        Assert.False(state.TryGet(
            observedAt.AddMilliseconds(200),
            out _,
            out _));
    }

    private sealed class FakeNative : INativeWindowApi
    {
        public nint Explorer { get; } = new(10);
        public uint ProcessId { get; } = 42;
        public string ProcessName { get; set; } = "explorer";

        public nint GetForegroundWindow() => Explorer;
        public nint GetFocusedWindow(nint foregroundWindow) => Explorer;
        public nint GetRootWindow(nint window) => Explorer;
        public uint GetProcessId(nint window) => ProcessId;
        public string? GetProcessName(uint processId) => ProcessName;
        public string GetClassName(nint window) => "CabinetWClass";
        public int GetControlId(nint window) => 0;
        public nint GetOwnerWindow(nint window) => nint.Zero;
        public WindowBounds GetWindowBounds(nint window) =>
            new(0, 0, 1200, 900);
        public bool HasDescendant(
            nint root,
            string className,
            int controlId) => false;
        public uint GetClipboardSequenceNumber() => 0;
    }
}
