using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordContextValidatorTests
{
    [Fact]
    public void AcceptsForegroundDiscordRenderer()
    {
        var native = new FakeNative();
        var validator = new DiscordContextValidator(native, native);

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
        var native = new FakeNative { ProcessName = "notepad" };
        var validator = new DiscordContextValidator(native, native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Fact]
    public void RejectsWindowWithoutRenderer()
    {
        var native = new FakeNative { Renderer = nint.Zero };
        var validator = new DiscordContextValidator(native, native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
    }

    [Fact]
    public void RejectsRendererFromAnotherRoot()
    {
        var native = new FakeNative { RendererRoot = new nint(999) };
        var validator = new DiscordContextValidator(native, native);

        Assert.False(validator.TryValidate(CreateTrigger(native), out _));
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

    private sealed class FakeNative : INativeWindowApi, IDiscordWindowApi
    {
        public nint Main { get; } = new(10);
        public nint Renderer { get; set; } = new(20);
        public nint RendererRoot { get; set; } = new(10);
        public uint ProcessId { get; } = 42;
        public string ProcessName { get; set; } = "Discord";

        public nint FindDescendant(nint root, string className) =>
            className == DiscordContextValidator.RendererClassName
                ? Renderer
                : nint.Zero;

        public nint GetForegroundWindow() => Main;

        public nint GetFocusedWindow(nint foregroundWindow) => Renderer;

        public nint GetRootWindow(nint window) =>
            window == Renderer ? RendererRoot : Main;

        public uint GetProcessId(nint window) => ProcessId;

        public string? GetProcessName(uint processId) => ProcessName;

        public string GetClassName(nint window) =>
            window == Main
                ? DiscordContextValidator.MainWindowClassName
                : DiscordContextValidator.RendererClassName;

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
