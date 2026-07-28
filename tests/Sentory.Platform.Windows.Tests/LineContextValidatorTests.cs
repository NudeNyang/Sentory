using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class LineContextValidatorTests
{
    [Fact]
    public void AcceptsForegroundLineMainWindow()
    {
        var native = new FakeNative();
        var validator = new LineContextValidator(native);

        var accepted = validator.TryValidate(
            CreateTrigger(native),
            out var context);

        Assert.True(accepted);
        Assert.Equal(native.Main, context.MainWindow);
        Assert.NotEmpty(context.ContextHash);
    }

    [Fact]
    public void AcceptsCurrentLineNativeQtWindowClass()
    {
        var native = new FakeNative
        {
            MainClass = "Qt663QWindowIcon"
        };
        var validator = new LineContextValidator(native);

        Assert.True(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Fact]
    public void ResolvesOwnedPhotoDialogBackToMainChatWindow()
    {
        var native = new FakeNative { UseOwnedPhotoDialog = true };
        var validator = new LineContextValidator(native);

        var accepted = validator.TryValidate(
            CreateTrigger(native),
            out var context);

        Assert.True(accepted);
        Assert.Equal(native.Main, context.MainWindow);
    }

    [Fact]
    public void RejectsAnotherProcess()
    {
        var native = new FakeNative { ProcessName = "notepad" };
        var validator = new LineContextValidator(native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Fact]
    public void RejectsAnotherTopLevelWindowClass()
    {
        var native = new FakeNative { MainClass = "QtWindow" };
        var validator = new LineContextValidator(native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Theory]
    [InlineData("QtWindow")]
    [InlineData("Qt663QWindow")]
    [InlineData("OtherQWindowIcon")]
    public void RejectsUnrelatedQtWindowClasses(string className)
    {
        var native = new FakeNative { MainClass = className };
        var validator = new LineContextValidator(native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
    }

    private static PasteTrigger CreateTrigger(FakeNative native) =>
        new(
            Guid.NewGuid(),
            native.GetForegroundWindow(),
            native.GetFocusedWindow(native.GetForegroundWindow()),
            native.ProcessId,
            12,
            DateTimeOffset.UtcNow,
            false);

    private sealed class FakeNative : INativeWindowApi
    {
        public nint Main { get; } = new(10);
        public nint PhotoDialog { get; } = new(20);
        public uint ProcessId { get; } = 42;
        public string ProcessName { get; set; } = "LINE";
        public string MainClass { get; set; } =
            LineContextValidator.MainWindowClassName;
        public bool UseOwnedPhotoDialog { get; set; }

        public nint GetForegroundWindow() =>
            UseOwnedPhotoDialog ? PhotoDialog : Main;
        public nint GetFocusedWindow(nint foregroundWindow) =>
            UseOwnedPhotoDialog ? PhotoDialog : Main;
        public nint GetRootWindow(nint window) =>
            window == PhotoDialog ? PhotoDialog : Main;
        public uint GetProcessId(nint window) => ProcessId;
        public string? GetProcessName(uint processId) => ProcessName;
        public string GetClassName(nint window) => MainClass;
        public int GetControlId(nint window) => 0;
        public nint GetOwnerWindow(nint window) =>
            window == PhotoDialog ? Main : nint.Zero;
        public WindowBounds GetWindowBounds(nint window) =>
            new(0, 0, 1200, 900);
        public bool HasDescendant(
            nint root,
            string className,
            int controlId) => false;
        public uint GetClipboardSequenceNumber() => 12;
    }
}
