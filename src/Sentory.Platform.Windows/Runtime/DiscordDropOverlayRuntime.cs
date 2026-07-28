using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public sealed class DiscordDropOverlayRuntime : IDisposable
{
    private readonly PassiveMessengerDropRuntime<DiscordDropTarget> _runtime;

    public DiscordDropOverlayRuntime(
        DiscordCaptureRuntime captureRuntime,
        Action<string, string>? diagnostic = null)
    {
        var native = new NativeWindowApi();
        var locator = new DiscordDropTargetLocator(native, native, native);
        _runtime = new PassiveMessengerDropRuntime<DiscordDropTarget>(
            native,
            native,
            new ExplorerSelectionReader(),
            () => captureRuntime.IsPaused,
            processName => string.Equals(
                processName,
                DiscordContextValidator.DiscordProcessName,
                StringComparison.OrdinalIgnoreCase),
            (x, y) => locator.FindAt(x, y, requireTopmost: true),
            async (target, paths) =>
                (await captureRuntime.RegisterNativeDroppedFilesAsync(
                    target,
                    paths)).ToString(),
            target => target.Bounds,
            target => $"window=0x{target.MainWindow.ToInt64():X}",
            "discord",
            diagnostic);
    }

    public void Start() => _runtime.Start();

    public void Dispose()
    {
        _runtime.Dispose();
        GC.SuppressFinalize(this);
    }
}
