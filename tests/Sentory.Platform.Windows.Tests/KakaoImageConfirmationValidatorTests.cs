using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class KakaoImageConfirmationValidatorTests
{
    [Fact]
    public void AcceptsOwnedImageConfirmationWindow()
    {
        var native = new FakeNative();
        var validator = new KakaoImageConfirmationValidator(native);

        Assert.True(validator.TryValidate(CreateContext(native), out var window));
        Assert.Equal(native.Preview, window);
    }

    [Fact]
    public void RejectsPreviewOwnedByAnotherWindow()
    {
        var native = new FakeNative
        {
            Owner = new nint(999)
        };
        var validator = new KakaoImageConfirmationValidator(native);

        Assert.False(validator.TryValidate(CreateContext(native), out _));
    }

    [Fact]
    public void RejectsWrongFocusedControl()
    {
        var native = new FakeNative
        {
            FocusClass = "Button"
        };
        var validator = new KakaoImageConfirmationValidator(native);

        Assert.False(validator.TryValidate(CreateContext(native), out _));
    }

    [Fact]
    public void RejectsUnchangedChatForeground()
    {
        var native = new FakeNative();
        native.Foreground = native.Chat;
        var validator = new KakaoImageConfirmationValidator(native);

        Assert.False(validator.TryValidate(CreateContext(native), out _));
    }

    private static ValidatedKakaoContext CreateContext(FakeNative native) =>
        new(
            Guid.NewGuid(),
            native.Chat,
            native.Input,
            native.ProcessId,
            native.ClipboardSequence,
            DateTimeOffset.UtcNow,
            "context");

    private sealed class FakeNative : INativeWindowApi
    {
        public nint Chat { get; } = new(10);
        public nint Input { get; } = new(11);
        public nint Preview { get; } = new(20);
        public nint Focus { get; } = new(21);
        public nint Foreground { get; set; } = new(20);
        public nint Owner { get; set; } = new(10);
        public uint ProcessId { get; } = 42;
        public uint ClipboardSequence { get; } = 7;
        public string FocusClass { get; set; } = "Edit";

        public nint GetForegroundWindow() => Foreground;

        public nint GetFocusedWindow(nint foregroundWindow) => Focus;

        public nint GetRootWindow(nint window) =>
            window == Focus ? Preview : window;

        public uint GetProcessId(nint window) => ProcessId;

        public string? GetProcessName(uint processId) => "KakaoTalk";

        public string GetClassName(nint window) =>
            window == Focus ? FocusClass : "EVA_Window_Dblclk";

        public int GetControlId(nint window) => 100;

        public nint GetOwnerWindow(nint window) => Owner;

        public WindowBounds GetWindowBounds(nint window) =>
            new(0, 0, 680, 800);

        public bool HasDescendant(
            nint root,
            string className,
            int controlId) =>
            className == "Edit" && controlId == 100;

        public uint GetClipboardSequenceNumber() => ClipboardSequence;
    }
}
