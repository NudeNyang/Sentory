using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordDropTargetLocatorTests
{
    [Fact]
    public void FindsVisibleDiscordWindowContainingCursor()
    {
        var native = new FakeNative();
        var locator = new DiscordDropTargetLocator(native, native, native);

        var target = locator.FindAt(450, 300);

        Assert.NotNull(target);
        Assert.Equal(native.Main, target.MainWindow);
        Assert.Equal(native.Renderer, target.RendererWindow);
        Assert.Equal(native.ProcessId, target.ProcessId);
        Assert.Equal(new WindowBounds(300, 120, 940, 820), target.Bounds);
    }

    [Fact]
    public void RejectsCursorOutsideDiscordWindow()
    {
        var native = new FakeNative();
        var locator = new DiscordDropTargetLocator(native, native, native);

        Assert.Null(locator.FindAt(100, 100));
    }

    [Fact]
    public void RejectsDiscordWindowWithoutRenderer()
    {
        var native = new FakeNative
        {
            HasRenderer = false
        };
        var locator = new DiscordDropTargetLocator(native, native, native);

        Assert.Null(locator.FindAt(450, 300));
    }

    [Fact]
    public void RejectsOccludedDiscordWindowWhenTopmostIsRequired()
    {
        var native = new FakeNative { IsOccluded = true };
        var locator = new DiscordDropTargetLocator(native, native, native);

        Assert.Null(locator.FindAt(450, 300, requireTopmost: true));
    }

    [Fact]
    public void ConfirmsReleaseOnlyWithinOriginalDiscordBounds()
    {
        var native = new FakeNative();
        var locator = new DiscordDropTargetLocator(native, native, native);
        var target = locator.FindAt(450, 300)!;

        Assert.True(locator.IsWithinTargetBounds(target, 450, 300));
        Assert.False(locator.IsWithinTargetBounds(target, 100, 100));
    }

    private sealed class FakeNative :
        INativeWindowApi,
        IDiscordWindowApi,
        IKakaoDropWindowApi
    {
        public nint Main { get; } = new(20);
        public nint Renderer { get; } = new(21);
        public uint ProcessId { get; } = 84;
        public bool HasRenderer { get; set; } = true;
        public bool IsOccluded { get; set; }

        public nint GetForegroundWindow() => Main;
        public nint GetFocusedWindow(nint foregroundWindow) => Renderer;
        public nint GetRootWindow(nint window) =>
            window == new nint(99) ? window : Main;
        public uint GetProcessId(nint window) => ProcessId;
        public string? GetProcessName(uint processId) => "Discord";
        public string GetClassName(nint window) =>
            window == Renderer
                ? DiscordContextValidator.RendererClassName
                : DiscordContextValidator.MainWindowClassName;
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
        public nint FindDescendant(nint root, string className) =>
            HasRenderer &&
            className == DiscordContextValidator.RendererClassName
                ? Renderer
                : nint.Zero;
        public nint FindDescendant(
            nint root,
            string className,
            int controlId) => nint.Zero;
        public bool IsWindowVisible(nint window) => true;
        public bool IsWindowMinimized(nint window) => false;
        public (int X, int Y) GetCursorPosition() => (450, 300);
        public nint GetWindowAtPoint(int x, int y) =>
            IsOccluded ? new nint(99) : Renderer;
        public bool IsLeftMouseButtonDown() => false;
        public bool IsEscapeKeyDown() => false;
        public bool PositionTopmostWindow(
            nint window,
            WindowBounds bounds) => true;
        public bool FocusWindowAndSendPaste(nint root, nint input) => true;
    }
}
