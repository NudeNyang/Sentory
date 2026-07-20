using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class KakaoDropTargetLocatorTests
{
    [Fact]
    public void FindsOnlyVerifiedIndividualChatContainingCursor()
    {
        var native = new FakeNative();
        var locator = new KakaoDropTargetLocator(native, native);

        var target = locator.FindAt(450, 300);

        Assert.NotNull(target);
        Assert.Equal(native.Chat, target.ChatRootWindow);
        Assert.Equal(native.Input, target.InputWindow);
        Assert.Equal(new WindowBounds(320, 650, 920, 790), target.InputBounds);
    }

    [Fact]
    public void RejectsCursorOutsideChatWindow()
    {
        var native = new FakeNative();
        var locator = new KakaoDropTargetLocator(native, native);

        Assert.Null(locator.FindAt(100, 100));
    }

    [Fact]
    public void RejectsKakaoWindowWithoutIndividualChatMessageList()
    {
        var native = new FakeNative
        {
            HasMessageList = false
        };
        var locator = new KakaoDropTargetLocator(native, native);

        Assert.Null(locator.FindAt(450, 300));
    }

    [Fact]
    public void ConfirmsReleaseOnlyWithinOriginalChatBounds()
    {
        var native = new FakeNative();
        var locator = new KakaoDropTargetLocator(native, native);
        var target = locator.FindAt(450, 300)!;

        Assert.True(locator.IsWithinTargetBounds(target, 450, 300));
        Assert.False(locator.IsWithinTargetBounds(target, 100, 100));
    }

    private sealed class FakeNative :
        INativeWindowApi,
        IKakaoDropWindowApi
    {
        public nint Chat { get; } = new(10);
        public nint Input { get; } = new(11);
        public uint ProcessId { get; } = 42;
        public bool HasMessageList { get; set; } = true;
        public nint WindowAtPoint { get; set; }

        public nint GetForegroundWindow() => Chat;
        public nint GetFocusedWindow(nint foregroundWindow) => Input;
        public nint GetRootWindow(nint window) =>
            window == new nint(999) ? new nint(999) : Chat;
        public uint GetProcessId(nint window) => ProcessId;
        public string? GetProcessName(uint processId) => "KakaoTalk";
        public string GetClassName(nint window) =>
            window == Input ? "RICHEDIT50W" : "EVA_Window_Dblclk";
        public int GetControlId(nint window) =>
            window == Input ? 1006 : 0;
        public nint GetOwnerWindow(nint window) => nint.Zero;
        public WindowBounds GetWindowBounds(nint window) =>
            window == Input
                ? new WindowBounds(320, 650, 920, 790)
                : new WindowBounds(300, 120, 940, 820);
        public bool HasDescendant(
            nint root,
            string className,
            int controlId) =>
            HasMessageList &&
            className == KakaoContextValidator.MessageListClassName &&
            controlId == KakaoContextValidator.MessageListControlId;
        public uint GetClipboardSequenceNumber() => 1;
        public IReadOnlyList<nint> EnumerateTopLevelWindows() => [Chat];
        public nint FindDescendant(
            nint root,
            string className,
            int controlId) =>
            className == KakaoContextValidator.InputClassName &&
            controlId == KakaoContextValidator.InputControlId
                ? Input
                : nint.Zero;
        public bool IsWindowVisible(nint window) => true;
        public bool IsWindowMinimized(nint window) => false;
        public (int X, int Y) GetCursorPosition() => (450, 300);
        public nint GetWindowAtPoint(int x, int y) =>
            WindowAtPoint == nint.Zero ? Input : WindowAtPoint;
        public bool IsEscapeKeyDown() => false;
        public bool IsLeftMouseButtonDown() => false;
        public bool PositionTopmostWindow(
            nint window,
            WindowBounds bounds) => true;
        public bool FocusWindowAndSendPaste(nint root, nint input) => true;
    }
}
