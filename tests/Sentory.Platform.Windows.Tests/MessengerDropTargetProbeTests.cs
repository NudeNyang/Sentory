using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class MessengerDropTargetProbeTests
{
    [Fact]
    public void MatchingProcessAllowsMessengerTargetScan()
    {
        var native = new FakeWindowApi("Discord");

        var matches = MessengerDropTargetProbe.IsProcessAt(
            native,
            native,
            (100, 200),
            "Discord");

        Assert.True(matches);
    }

    [Fact]
    public void UnrelatedProcessSkipsMessengerTargetScan()
    {
        var native = new FakeWindowApi("explorer");

        var matches = MessengerDropTargetProbe.IsProcessAt(
            native,
            native,
            (100, 200),
            "Discord");

        Assert.False(matches);
    }

    private sealed class FakeWindowApi(string processName) :
        INativeWindowApi,
        IKakaoDropWindowApi
    {
        public nint GetWindowAtPoint(int x, int y) => new(10);
        public nint GetRootWindow(nint window) => new(20);
        public uint GetProcessId(nint window) => 30;
        public string? GetProcessName(uint processId) => processName;

        public nint GetForegroundWindow() => throw new NotSupportedException();
        public nint GetFocusedWindow(nint foregroundWindow) => throw new NotSupportedException();
        public string GetClassName(nint window) => throw new NotSupportedException();
        public int GetControlId(nint window) => throw new NotSupportedException();
        public nint GetOwnerWindow(nint window) => throw new NotSupportedException();
        public WindowBounds GetWindowBounds(nint window) => throw new NotSupportedException();
        public bool HasDescendant(nint root, string className, int controlId) => throw new NotSupportedException();
        public uint GetClipboardSequenceNumber() => throw new NotSupportedException();
        public IReadOnlyList<nint> EnumerateTopLevelWindows() => throw new NotSupportedException();
        public nint FindDescendant(nint root, string className, int controlId) => throw new NotSupportedException();
        public bool IsWindowVisible(nint window) => throw new NotSupportedException();
        public bool IsWindowMinimized(nint window) => throw new NotSupportedException();
        public (int X, int Y) GetCursorPosition() => throw new NotSupportedException();
        public bool IsLeftMouseButtonDown() => throw new NotSupportedException();
        public bool IsEscapeKeyDown() => throw new NotSupportedException();
        public bool PositionTopmostWindow(nint window, WindowBounds bounds) => throw new NotSupportedException();
        public bool FocusWindowAndSendPaste(nint root, nint input) => throw new NotSupportedException();
    }
}
