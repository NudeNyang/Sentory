namespace Sentory.Platform.Windows.Interop;

public sealed class LineDropTargetLocator(
    INativeWindowApi native,
    IKakaoDropWindowApi dropWindows)
{
    public LineDropTarget? FindAt(
        int cursorX,
        int cursorY,
        bool requireTopmost = false) =>
        FindAt(
            cursorX,
            cursorY,
            requireTopmost,
            allowForegroundFallback: false);

    public LineDropTarget? FindReleaseAt(int cursorX, int cursorY) =>
        FindAt(
            cursorX,
            cursorY,
            requireTopmost: true,
            allowForegroundFallback: true);

    private LineDropTarget? FindAt(
        int cursorX,
        int cursorY,
        bool requireTopmost,
        bool allowForegroundFallback)
    {
        foreach (var target in FindVisibleMainWindows())
        {
            if (!Contains(target.Bounds, cursorX, cursorY))
            {
                continue;
            }

            if (requireTopmost &&
                !IsTopmostOrLineOwnedSurface(
                    target.MainWindow,
                    target.ProcessId,
                    cursorX,
                    cursorY) &&
                !(allowForegroundFallback &&
                  IsForegroundLineSurface(
                      target.MainWindow,
                      target.ProcessId)))
            {
                continue;
            }

            return target;
        }

        return null;
    }

    private bool IsForegroundLineSurface(
        nint mainWindow,
        uint lineProcessId)
    {
        var foregroundRoot = native.GetRootWindow(
            native.GetForegroundWindow());
        return foregroundRoot == mainWindow ||
               (foregroundRoot != nint.Zero &&
                native.GetProcessId(foregroundRoot) == lineProcessId &&
                string.Equals(
                    native.GetProcessName(lineProcessId),
                    LineContextValidator.ProcessName,
                    StringComparison.OrdinalIgnoreCase));
    }

    public LineDropTarget? FindVisibleMainWindow() =>
        FindVisibleMainWindows()
            .OrderByDescending(target =>
                (long)target.Bounds.Width * target.Bounds.Height)
            .FirstOrDefault();

    private IEnumerable<LineDropTarget> FindVisibleMainWindows()
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
                !LineContextValidator.IsSupportedMainWindowClass(
                    native.GetClassName(root)))
            {
                continue;
            }

            var bounds = native.GetWindowBounds(root);
            if (bounds.Width < 320 || bounds.Height < 320)
            {
                continue;
            }

            yield return new LineDropTarget(root, processId, bounds);
        }
    }

    private bool IsTopmostOrLineOwnedSurface(
        nint mainWindow,
        uint lineProcessId,
        int cursorX,
        int cursorY)
    {
        var windowAtPoint = dropWindows.GetWindowAtPoint(cursorX, cursorY);
        var rootAtPoint = native.GetRootWindow(windowAtPoint);
        return rootAtPoint == mainWindow ||
               (rootAtPoint != nint.Zero &&
                native.GetProcessId(rootAtPoint) == lineProcessId &&
                string.Equals(
                    native.GetProcessName(lineProcessId),
                    LineContextValidator.ProcessName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool Contains(WindowBounds bounds, int x, int y) =>
        x >= bounds.Left &&
        x < bounds.Right &&
        y >= bounds.Top &&
        y < bounds.Bottom;
}
