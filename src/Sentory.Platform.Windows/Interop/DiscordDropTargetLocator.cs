namespace Sentory.Platform.Windows.Interop;

public sealed record DiscordDropTarget(
    nint MainWindow,
    nint RendererWindow,
    uint ProcessId,
    WindowBounds Bounds);

public sealed class DiscordDropTargetLocator(
    INativeWindowApi native,
    IDiscordWindowApi discordWindows,
    IKakaoDropWindowApi dropWindows)
{
    public DiscordDropTarget? FindAt(int cursorX, int cursorY)
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
                    DiscordContextValidator.DiscordProcessName,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    native.GetClassName(root),
                    DiscordContextValidator.MainWindowClassName,
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

            var renderer = discordWindows.FindDescendant(
                root,
                DiscordContextValidator.RendererClassName);
            if (renderer == nint.Zero ||
                native.GetRootWindow(renderer) != root ||
                native.GetProcessId(renderer) != processId)
            {
                continue;
            }

            return new DiscordDropTarget(
                root,
                renderer,
                processId,
                bounds);
        }

        return null;
    }

    public bool IsWithinTargetBounds(
        DiscordDropTarget target,
        int cursorX,
        int cursorY) =>
        Contains(target.Bounds, cursorX, cursorY);

    private static bool Contains(WindowBounds bounds, int x, int y) =>
        x >= bounds.Left &&
        x < bounds.Right &&
        y >= bounds.Top &&
        y < bounds.Bottom;
}
