using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class WeChatContextValidatorTests
{
    [Theory]
    [InlineData("Weixin")]
    [InlineData("WeChat")]
    public void AcceptsSupportedWeChatProcessNames(string processName)
    {
        var native = new FakeNative { ProcessName = processName };
        var validator = new WeChatContextValidator(native);

        var accepted = validator.TryValidate(
            CreateTrigger(native),
            out var context);

        Assert.True(accepted);
        Assert.Equal(native.Main, context.MainWindow);
        Assert.NotEmpty(context.ContextHash);
    }

    [Theory]
    [InlineData("Qt51514QWindowIcon")]
    [InlineData("Qt663QWindowIcon")]
    public void AcceptsVersionedQtMainWindowClasses(string className)
    {
        var native = new FakeNative { MainClass = className };
        var validator = new WeChatContextValidator(native);

        Assert.True(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Fact]
    public void ResolvesOwnedPreviewWindowBackToMainWindow()
    {
        var native = new FakeNative { UseOwnedDialog = true };
        var validator = new WeChatContextValidator(native);

        var accepted = validator.TryValidate(
            CreateTrigger(native),
            out var context);

        Assert.True(accepted);
        Assert.Equal(native.Main, context.MainWindow);
    }

    [Theory]
    [InlineData("WeChatAppEx")]
    [InlineData("notepad")]
    public void RejectsHelperAndUnrelatedProcesses(string processName)
    {
        var native = new FakeNative { ProcessName = processName };
        var validator = new WeChatContextValidator(native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Theory]
    [InlineData("mmui::MainWindow")]
    [InlineData("Qt51514QWindow")]
    [InlineData("Chrome_WidgetWin_1")]
    public void RejectsUnsupportedNativeWindowClasses(string className)
    {
        var native = new FakeNative { MainClass = className };
        var validator = new WeChatContextValidator(native);

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
        public nint Dialog { get; } = new(20);
        public uint ProcessId { get; } = 42;
        public string ProcessName { get; set; } = "Weixin";
        public string MainClass { get; set; } = "Qt51514QWindowIcon";
        public bool UseOwnedDialog { get; set; }

        public nint GetForegroundWindow() => UseOwnedDialog ? Dialog : Main;
        public nint GetFocusedWindow(nint foregroundWindow) => foregroundWindow;
        public nint GetRootWindow(nint window) => window;
        public uint GetProcessId(nint window) => ProcessId;
        public string? GetProcessName(uint processId) => ProcessName;
        public string GetClassName(nint window) =>
            window == Dialog ? "mmui::PreviewWindow" : MainClass;
        public int GetControlId(nint window) => 0;
        public nint GetOwnerWindow(nint window) =>
            window == Dialog ? Main : nint.Zero;
        public WindowBounds GetWindowBounds(nint window) =>
            new(0, 0, 1200, 900);
        public bool HasDescendant(
            nint root,
            string className,
            int controlId) => false;
        public uint GetClipboardSequenceNumber() => 12;
    }
}
