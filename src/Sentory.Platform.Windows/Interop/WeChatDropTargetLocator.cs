namespace Sentory.Platform.Windows.Interop;

public sealed class WeChatDropTargetLocator(
    INativeWindowApi native,
    IKakaoDropWindowApi dropWindows)
{
    public WeChatDropTarget? FindAt(
        int cursorX,
        int cursorY,
        bool requireTopmost = false) =>
        FindAt(
            cursorX,
            cursorY,
            requireTopmost,
            allowForegroundFallback: false);

    public WeChatDropTarget? FindReleaseAt(int cursorX, int cursorY) =>
        FindAt(
            cursorX,
            cursorY,
            requireTopmost: true,
            allowForegroundFallback: true);

    private WeChatDropTarget? FindAt(
        int cursorX,
        int cursorY,
        bool requireTopmost,
        bool allowForegroundFallback)
    {
        foreach (var root in dropWindows.EnumerateTopLevelWindows())
        {
            if (!dropWindows.IsWindowVisible(root) ||
                dropWindows.IsWindowMinimized(root) ||
                native.GetRootWindow(root) != root)
            {
                continue;
            }

            var processId = native.GetProcessId(root);
            if (processId == 0 ||
                !WeChatContextValidator.IsSupportedProcessName(
                    native.GetProcessName(processId)) ||
                !WeChatContextValidator.IsSupportedMainWindowClass(
                    native.GetClassName(root)))
            {
                continue;
            }

            var bounds = native.GetWindowBounds(root);
            if (!Contains(bounds, cursorX, cursorY) ||
                bounds.Width < 320 ||
                bounds.Height < 320)
            {
                continue;
            }

            if (requireTopmost &&
                !IsTopmostOrWeChatOwnedSurface(
                    root,
                    processId,
                    cursorX,
                    cursorY) &&
                !(allowForegroundFallback &&
                  IsForegroundWeChatSurface(root, processId)))
            {
                continue;
            }

            return new WeChatDropTarget(root, processId, bounds);
        }

        return null;
    }

    private bool IsForegroundWeChatSurface(
        nint mainWindow,
        uint weChatProcessId)
    {
        var foregroundRoot = native.GetRootWindow(
            native.GetForegroundWindow());
        return foregroundRoot == mainWindow ||
               (foregroundRoot != nint.Zero &&
                native.GetProcessId(foregroundRoot) == weChatProcessId &&
                WeChatContextValidator.IsSupportedProcessName(
                    native.GetProcessName(weChatProcessId)));
    }

    private bool IsTopmostOrWeChatOwnedSurface(
        nint mainWindow,
        uint weChatProcessId,
        int cursorX,
        int cursorY)
    {
        var windowAtPoint = dropWindows.GetWindowAtPoint(cursorX, cursorY);
        var rootAtPoint = native.GetRootWindow(windowAtPoint);
        return rootAtPoint == mainWindow ||
               (rootAtPoint != nint.Zero &&
                (native.GetProcessId(rootAtPoint) == weChatProcessId ||
                 WeChatContextValidator.IsSupportedDropSurfaceProcessName(
                     native.GetProcessName(
                         native.GetProcessId(rootAtPoint)))));
    }

    private static bool Contains(WindowBounds bounds, int x, int y) =>
        x >= bounds.Left &&
        x < bounds.Right &&
        y >= bounds.Top &&
        y < bounds.Bottom;
}
