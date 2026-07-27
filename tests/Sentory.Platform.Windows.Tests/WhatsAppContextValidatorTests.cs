using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class WhatsAppContextValidatorTests
{
    [Fact]
    public void AcceptsFocusedWhatsAppWebView()
    {
        var native = new FakeNative();
        var validator = new WhatsAppContextValidator(native);

        var accepted = validator.TryValidate(
            CreateTrigger(native),
            out var context);

        Assert.True(accepted);
        Assert.Equal(native.Main, context.MainWindow);
        Assert.Equal(native.Renderer, context.RendererWindow);
        Assert.NotEmpty(context.ContextHash);
    }

    [Fact]
    public void RejectsAnotherProcess()
    {
        var native = new FakeNative { ProcessName = "WhatsApp" };
        var validator = new WhatsAppContextValidator(native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Fact]
    public void RejectsFocusOutsideWebView()
    {
        var native = new FakeNative { FocusClass = "InputSiteWindowClass" };
        var validator = new WhatsAppContextValidator(native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Fact]
    public void RejectsRendererFromAnotherRoot()
    {
        var native = new FakeNative { RendererRoot = new nint(999) };
        var validator = new WhatsAppContextValidator(native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Theory]
    [InlineData(1180, 870, true)]
    [InlineData(1050, 870, false)]
    [InlineData(1180, 650, false)]
    [InlineData(1200, 870, false)]
    public void SendButtonPolicyRestrictsBottomRightHotZone(
        int x,
        int y,
        bool expected)
    {
        Assert.Equal(
            expected,
            WhatsAppSendButtonPolicy.IsWithin(
                new WindowBounds(0, 0, 1200, 900),
                x,
                y));
    }

    private static PasteTrigger CreateTrigger(FakeNative native) =>
        new(
            Guid.NewGuid(),
            native.Main,
            native.Renderer,
            native.ProcessId,
            12,
            DateTimeOffset.UtcNow,
            false);

    private sealed class FakeNative : INativeWindowApi
    {
        public nint Main { get; } = new(10);
        public nint Renderer { get; } = new(20);
        public nint RendererRoot { get; set; } = new(10);
        public uint ProcessId { get; } = 42;
        public string ProcessName { get; set; } = "WhatsApp.Root";
        public string FocusClass { get; set; } =
            WhatsAppContextValidator.RendererClassName;

        public nint GetForegroundWindow() => Main;
        public nint GetFocusedWindow(nint foregroundWindow) => Renderer;
        public nint GetRootWindow(nint window) =>
            window == Renderer ? RendererRoot : Main;
        public uint GetProcessId(nint window) => ProcessId;
        public string? GetProcessName(uint processId) => ProcessName;
        public string GetClassName(nint window) =>
            window == Main
                ? WhatsAppContextValidator.MainWindowClassName
                : FocusClass;
        public int GetControlId(nint window) => 0;
        public nint GetOwnerWindow(nint window) => nint.Zero;
        public WindowBounds GetWindowBounds(nint window) =>
            new(0, 0, 1200, 900);
        public bool HasDescendant(
            nint root,
            string className,
            int controlId) => false;
        public uint GetClipboardSequenceNumber() => 12;
    }
}
