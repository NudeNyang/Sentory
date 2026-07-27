namespace Sentory.Platform.Windows.Interop;

public sealed class SlackDropTargetLocator(
    INativeWindowApi native,
    IDiscordWindowApi chromiumWindows,
    IKakaoDropWindowApi dropWindows)
{
    public SlackDropTarget? FindAt(
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
                    SlackContextValidator.SlackProcessName,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    native.GetClassName(root),
                    SlackContextValidator.MainWindowClassName,
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

            var renderer = chromiumWindows.FindDescendant(
                root,
                SlackContextValidator.RendererClassName);
            if (renderer == nint.Zero ||
                native.GetRootWindow(renderer) != root ||
                native.GetProcessId(renderer) != processId)
            {
                continue;
            }

            return new SlackDropTarget(
                root,
                renderer,
                processId,
                bounds);
        }

        return null;
    }

    private static bool Contains(WindowBounds bounds, int x, int y) =>
        x >= bounds.Left &&
        x < bounds.Right &&
        y >= bounds.Top &&
        y < bounds.Bottom;
}
