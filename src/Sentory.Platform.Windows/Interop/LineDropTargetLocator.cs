namespace Sentory.Platform.Windows.Interop;

public sealed class LineDropTargetLocator(
    INativeWindowApi native,
    IKakaoDropWindowApi dropWindows)
{
    public LineDropTarget? FindAt(
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
                    LineContextValidator.ProcessName,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    native.GetClassName(root),
                    LineContextValidator.MainWindowClassName,
                    StringComparison.Ordinal))
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
                native.GetRootWindow(
                    dropWindows.GetWindowAtPoint(cursorX, cursorY)) != root)
            {
                continue;
            }

            return new LineDropTarget(root, processId, bounds);
        }

        return null;
    }

    private static bool Contains(WindowBounds bounds, int x, int y) =>
        x >= bounds.Left &&
        x < bounds.Right &&
        y >= bounds.Top &&
        y < bounds.Bottom;
}
