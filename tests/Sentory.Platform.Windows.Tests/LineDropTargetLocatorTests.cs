using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class LineDropTargetLocatorTests
{
    [Fact]
    public void FindsCurrentLineNativeQtWindowContainingCursor()
    {
        var native = new FakeNative();
        var locator = new LineDropTargetLocator(native, native);

        var target = locator.FindAt(450, 300, requireTopmost: true);

        Assert.NotNull(target);
        Assert.Equal(native.Main, target.MainWindow);
    }

    [Fact]
    public void RejectsCursorOutsideWindow()
    {
        var native = new FakeNative();
        var locator = new LineDropTargetLocator(native, native);

        Assert.Null(locator.FindAt(100, 100));
    }

    [Fact]
    public void RejectsOccludedLineWindowWhenTopmostIsRequired()
    {
        var native = new FakeNative { IsOccluded = true };
        var locator = new LineDropTargetLocator(native, native);

        Assert.Null(locator.FindAt(450, 300, requireTopmost: true));
    }

    [Fact]
    public void AcceptsLineOwnedDragSurfaceAboveMainWindow()
    {
        var native = new FakeNative { HasLineDragSurface = true };
        var locator = new LineDropTargetLocator(native, native);

        var target = locator.FindAt(450, 300, requireTopmost: true);

        Assert.NotNull(target);
        Assert.Equal(native.Main, target.MainWindow);
    }

    private sealed class FakeNative :
        INativeWindowApi,
        IKakaoDropWindowApi
    {
        public nint Main { get; } = new(20);
        public uint ProcessId { get; } = 84;
        public bool IsOccluded { get; set; }
        public bool HasLineDragSurface { get; set; }

        public nint GetForegroundWindow() => Main;
        public nint GetFocusedWindow(nint foregroundWindow) => Main;
        public nint GetRootWindow(nint window) =>
            window == new nint(98) || window == new nint(99)
                ? window
                : Main;
        public uint GetProcessId(nint window) =>
            window == new nint(99) ? 999u : ProcessId;
        public string? GetProcessName(uint processId) =>
            processId == 999 ? "notepad" : "LINE";
        public string GetClassName(nint window) => "Qt663QWindowIcon";
        public int GetControlId(nint window) => 0;
        public nint GetOwnerWindow(nint window) => nint.Zero;
        public WindowBounds GetWindowBounds(nint window) =>
            new(300, 120, 940, 820);
        public bool HasDescendant(
            nint root,
            string className,
            int controlId) => false;
        public uint GetClipboardSequenceNumber() => 1;
        public IReadOnlyList<nint> EnumerateTopLevelWindows() => [Main];
        public nint FindDescendant(
            nint root,
            string className,
            int controlId) => nint.Zero;
        public bool IsWindowVisible(nint window) => true;
        public bool IsWindowMinimized(nint window) => false;
        public (int X, int Y) GetCursorPosition() => (450, 300);
        public nint GetWindowAtPoint(int x, int y) =>
            IsOccluded
                ? new nint(99)
                : HasLineDragSurface
                    ? new nint(98)
                    : Main;
        public bool IsLeftMouseButtonDown() => false;
        public bool IsEscapeKeyDown() => false;
        public bool PositionTopmostWindow(
            nint window,
            WindowBounds bounds) => true;
        public bool FocusWindowAndSendPaste(nint root, nint input) => true;
    }
}
