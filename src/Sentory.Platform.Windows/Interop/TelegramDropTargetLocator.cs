namespace Sentory.Platform.Windows.Interop;

public sealed class TelegramDropTargetLocator(
    INativeWindowApi native,
    IKakaoDropWindowApi dropWindows)
{
    public TelegramDropTarget? FindAt(
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
                !string.Equals(
                    native.GetProcessName(processId),
                    TelegramContextValidator.ProcessName,
                    StringComparison.OrdinalIgnoreCase) ||
                !TelegramContextValidator.IsSupportedMainWindowClass(
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
                !IsTopmostOrTelegramOwnedSurface(
                    root,
                    processId,
                    cursorX,
                    cursorY))
            {
                continue;
            }

            return new TelegramDropTarget(root, processId, bounds);
        }

        return null;
    }

    private bool IsTopmostOrTelegramOwnedSurface(
        nint mainWindow,
        uint telegramProcessId,
        int cursorX,
        int cursorY)
    {
        var windowAtPoint = dropWindows.GetWindowAtPoint(cursorX, cursorY);
        var rootAtPoint = native.GetRootWindow(windowAtPoint);
        return rootAtPoint == mainWindow ||
               (rootAtPoint != nint.Zero &&
                native.GetProcessId(rootAtPoint) == telegramProcessId &&
                string.Equals(
                    native.GetProcessName(telegramProcessId),
                    TelegramContextValidator.ProcessName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool Contains(WindowBounds bounds, int x, int y) =>
        x >= bounds.Left &&
        x < bounds.Right &&
        y >= bounds.Top &&
        y < bounds.Bottom;
}
