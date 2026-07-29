using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class WeChatDropTargetLocatorTests
{
    [Fact]
    public void FindsVisibleWeChatWindowContainingCursor()
    {
        var native = new FakeNative();
        var locator = new WeChatDropTargetLocator(native, native);

        var target = locator.FindAt(450, 300, requireTopmost: true);

        Assert.NotNull(target);
        Assert.Equal(native.Main, target.MainWindow);
    }

    [Fact]
    public void RejectsOccludedWeChatWindowWhenTopmostIsRequired()
    {
        var native = new FakeNative { IsOccluded = true };
        var locator = new WeChatDropTargetLocator(native, native);

        Assert.Null(locator.FindAt(450, 300, requireTopmost: true));
    }

    [Fact]
    public void AcceptsWeChatOwnedDragSurfaceAboveMainWindow()
    {
        var native = new FakeNative { HasWeChatDragSurface = true };
        var locator = new WeChatDropTargetLocator(native, native);

        var target = locator.FindAt(450, 300, requireTopmost: true);

        Assert.NotNull(target);
        Assert.Equal(native.Main, target.MainWindow);
    }

    [Fact]
    public void AcceptsWeChatHelperUploadSurfaceAboveMainWindow()
    {
        var native = new FakeNative { HasWeChatHelperSurface = true };
        var locator = new WeChatDropTargetLocator(native, native);

        var target = locator.FindAt(450, 300, requireTopmost: true);

        Assert.NotNull(target);
        Assert.Equal(native.Main, target.MainWindow);
    }

    [Fact]
    public void ReleaseAcceptsForegroundWeChatBehindTransientDragSurface()
    {
        var native = new FakeNative { HasTransientDragSurface = true };
        var locator = new WeChatDropTargetLocator(native, native);

        var target = locator.FindReleaseAt(450, 300);

        Assert.NotNull(target);
        Assert.Equal(native.Main, target.MainWindow);
    }

    [Fact]
    public void ReleaseRejectsTransientSurfaceWhenWeChatIsNotForeground()
    {
        var native = new FakeNative
        {
            HasTransientDragSurface = true,
            IsForegroundOtherApp = true
        };
        var locator = new WeChatDropTargetLocator(native, native);

        Assert.Null(locator.FindReleaseAt(450, 300));
    }

    private sealed class FakeNative : INativeWindowApi, IKakaoDropWindowApi
    {
        public nint Main { get; } = new(20);
        public uint ProcessId { get; } = 84;
        public bool IsOccluded { get; set; }
        public bool HasWeChatDragSurface { get; set; }
        public bool HasWeChatHelperSurface { get; set; }
        public bool HasTransientDragSurface { get; set; }
        public bool IsForegroundOtherApp { get; set; }

        public nint GetForegroundWindow() =>
            IsForegroundOtherApp ? new nint(99) : Main;
        public nint GetFocusedWindow(nint foregroundWindow) => nint.Zero;
        public nint GetRootWindow(nint window) =>
            window == new nint(97) ||
            window == new nint(98) ||
            window == new nint(99)
                ? window
                : Main;
        public uint GetProcessId(nint window) =>
            window == new nint(97)
                ? 777u
                : window == new nint(99)
                    ? 999u
                    : ProcessId;
        public string? GetProcessName(uint processId) =>
            processId == 777
                ? "WeChatAppEx"
                : processId == 999
                    ? "notepad"
                    : "Weixin";
        public string GetClassName(nint window) => "Qt51514QWindowIcon";
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
                : HasTransientDragSurface
                    ? new nint(99)
                    : HasWeChatHelperSurface
                        ? new nint(97)
                    : HasWeChatDragSurface
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
