using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class KakaoContextValidatorTests
{
    [Fact]
    public void AcceptsOnlyVerifiedIndividualChatInput()
    {
        var native = FakeNative.ValidKakaoChat();
        var validator = new KakaoContextValidator(native);

        var success = validator.TryValidate(
            CreateTrigger(native),
            out var context);

        Assert.True(success);
        Assert.Equal(native.Root, context.ChatRootWindow);
        Assert.Equal(native.Input, context.InputWindow);
        Assert.NotEmpty(context.ContextHash);
    }

    [Fact]
    public void RejectsSameControlShapeInAnotherProcess()
    {
        var native = FakeNative.ValidKakaoChat();
        native.ProcessName = "notepad";
        var validator = new KakaoContextValidator(native);

        Assert.False(validator.TryValidate(
            CreateTrigger(native),
            out _));
    }

    [Fact]
    public void RejectsKakaoMainSearchOrOtherEditControl()
    {
        var native = FakeNative.ValidKakaoChat();
        native.ControlId = 123;
        var validator = new KakaoContextValidator(native);

        Assert.False(validator.TryValidate(
            CreateTrigger(native),
            out _));
    }

    [Fact]
    public void RejectsWindowWithoutMessageList()
    {
        var native = FakeNative.ValidKakaoChat();
        native.HasMessageList = false;
        var validator = new KakaoContextValidator(native);

        Assert.False(validator.TryValidate(
            CreateTrigger(native),
            out _));
    }

    [Fact]
    public void RejectsFocusFromDifferentRoot()
    {
        var native = FakeNative.ValidKakaoChat();
        native.FocusRoot = new nint(999);
        var validator = new KakaoContextValidator(native);

        Assert.False(validator.TryValidate(
            CreateTrigger(native),
            out _));
    }

    [Fact]
    public void AcceptsVerifiedDropTargetWithoutForegroundFocus()
    {
        var native = FakeNative.ValidKakaoChat();
        var validator = new KakaoContextValidator(native);
        var target = new KakaoDropTarget(
            native.Root,
            native.Input,
            native.ProcessId,
            new WindowBounds(0, 0, 680, 800),
            new WindowBounds(0, 600, 680, 800));

        var success = validator.TryValidateTarget(
            target,
            12,
            DateTimeOffset.UtcNow,
            out var context);

        Assert.True(success);
        Assert.Equal(native.Root, context.ChatRootWindow);
        Assert.Equal(native.Input, context.InputWindow);
        Assert.Equal((uint)12, context.ClipboardSequenceNumber);
    }

    private static PasteTrigger CreateTrigger(FakeNative native) =>
        new(
            Guid.NewGuid(),
            native.Foreground,
            native.Input,
            native.ProcessId,
            10,
            DateTimeOffset.UtcNow,
            false);

    private sealed class FakeNative : INativeWindowApi
    {
        public nint Foreground { get; } = new(10);
        public nint Root { get; } = new(20);
        public nint Input { get; } = new(30);
        public nint FocusRoot { get; set; } = new(20);
        public uint ProcessId { get; } = 42;
        public string ProcessName { get; set; } = "KakaoTalk";
        public string ClassName { get; set; } = "RICHEDIT50W";
        public int ControlId { get; set; } = 1006;
        public bool HasMessageList { get; set; } = true;

        public static FakeNative ValidKakaoChat() => new();

        public nint GetForegroundWindow() => Foreground;

        public nint GetFocusedWindow(nint foregroundWindow) => Input;

        public nint GetRootWindow(nint window) =>
            window == Input ? FocusRoot : Root;

        public uint GetProcessId(nint window) => ProcessId;

        public string? GetProcessName(uint processId) => ProcessName;

        public string GetClassName(nint window) => ClassName;

        public int GetControlId(nint window) => ControlId;

        public nint GetOwnerWindow(nint window) => nint.Zero;

        public WindowBounds GetWindowBounds(nint window) =>
            new(0, 0, 680, 800);

        public bool HasDescendant(
            nint root,
            string className,
            int controlId) =>
            HasMessageList &&
            className == KakaoContextValidator.MessageListClassName &&
            controlId == KakaoContextValidator.MessageListControlId;

        public uint GetClipboardSequenceNumber() => 10;
    }
}
