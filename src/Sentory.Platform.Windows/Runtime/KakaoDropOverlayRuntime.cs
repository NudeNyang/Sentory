using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public sealed class KakaoDropOverlayRuntime : IDisposable
{
    private readonly PassiveMessengerDropRuntime<KakaoDropTarget> _runtime;

    public KakaoDropOverlayRuntime(
        KakaoCaptureRuntime captureRuntime,
        Func<bool> isDarkTheme,
        Func<string> headingText,
        Func<string> descriptionText,
        Action<string, string>? diagnostic = null)
    {
        _ = isDarkTheme;
        _ = headingText;
        _ = descriptionText;
        var native = new NativeWindowApi();
        var locator = new KakaoDropTargetLocator(native, native);
        _runtime = new PassiveMessengerDropRuntime<KakaoDropTarget>(
            native,
            native,
            new ExplorerSelectionReader(),
            () => captureRuntime.IsPaused,
            processName => string.Equals(
                processName,
                KakaoContextValidator.KakaoProcessName,
                StringComparison.OrdinalIgnoreCase),
            (x, y) => locator.FindAt(x, y, requireTopmost: true),
            async (target, paths) =>
                (await captureRuntime.CaptureNativeDroppedFilesAsync(
                    target,
                    paths)).ToString(),
            target => target.ChatBounds,
            target =>
                $"chat=0x{target.ChatRootWindow.ToInt64():X}",
            "kakao",
            diagnostic);
    }

    public void Start() => _runtime.Start();

    public void Dispose()
    {
        _runtime.Dispose();
        GC.SuppressFinalize(this);
    }
}
