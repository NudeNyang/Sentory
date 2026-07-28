namespace Sentory.Platform.Windows.Interop;

public sealed class WeChatDropTargetLocator(
    INativeWindowApi native,
    IKakaoDropWindowApi dropWindows)
{
    public WeChatDropTarget? FindAt(
        int cursorX,
        int cursorY,
        bool requireTopmost = false)
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
                    cursorY))
            {
                continue;
            }

            return new WeChatDropTarget(root, processId, bounds);
        }

        return null;
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
                native.GetProcessId(rootAtPoint) == weChatProcessId &&
                WeChatContextValidator.IsSupportedProcessName(
                    native.GetProcessName(weChatProcessId)));
    }

    private static bool Contains(WindowBounds bounds, int x, int y) =>
        x >= bounds.Left &&
        x < bounds.Right &&
        y >= bounds.Top &&
        y < bounds.Bottom;
}
