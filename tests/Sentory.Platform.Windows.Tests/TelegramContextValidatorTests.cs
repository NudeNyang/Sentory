using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class TelegramContextValidatorTests
{
    [Fact]
    public void AcceptsTelegramQtMainWindowWithoutFocusedChild()
    {
        var native = new FakeNative();
        var validator = new TelegramContextValidator(native);

        var accepted = validator.TryValidate(
            CreateTrigger(native),
            out var context);

        Assert.True(accepted);
        Assert.Equal(native.Main, context.MainWindow);
        Assert.NotEmpty(context.ContextHash);
    }

    [Fact]
    public void AcceptsQtClassVersionChanges()
    {
        Assert.True(
            TelegramContextValidator.IsSupportedMainWindowClass(
                "Qt6129QWindowIcon"));
    }

    [Fact]
    public void ResolvesOwnedPhotoDialogBackToMainChatWindow()
    {
        var native = new FakeNative { UseOwnedPhotoDialog = true };
        var validator = new TelegramContextValidator(native);

        var accepted = validator.TryValidate(
            CreateTrigger(native),
            out var context);

        Assert.True(accepted);
        Assert.Equal(native.Main, context.MainWindow);
    }

    [Theory]
    [InlineData("Telegram", "Chrome_WidgetWin_1")]
    [InlineData("TelegramUpdater", "Qt51519QWindowIcon")]
    [InlineData("explorer", "Qt51519QWindowIcon")]
    public void RejectsUnsupportedProcessOrWindowClass(
        string processName,
        string windowClass)
    {
        var native = new FakeNative
        {
            ProcessName = processName,
            MainClass = windowClass
        };
        var validator = new TelegramContextValidator(native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Theory]
    [InlineData(760, 820, true)]
    [InlineData(700, 650, false)]
    [InlineData(500, 820, false)]
    [InlineData(1200, 820, false)]
    public void SendButtonPolicyRestrictsLowerConversationArea(
        int x,
        int y,
        bool expected)
    {
        Assert.Equal(
            expected,
            TelegramSendButtonPolicy.IsWithin(
                new WindowBounds(0, 0, 1200, 900),
                x,
                y));
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
        public string ProcessName { get; set; } = "Telegram";
        public string MainClass { get; set; } = "Qt51519QWindowIcon";
        public bool UseOwnedPhotoDialog { get; set; }

        public nint GetForegroundWindow() =>
            UseOwnedPhotoDialog ? PhotoDialog : Main;
        public nint GetFocusedWindow(nint foregroundWindow) =>
            UseOwnedPhotoDialog ? PhotoDialog : nint.Zero;
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
