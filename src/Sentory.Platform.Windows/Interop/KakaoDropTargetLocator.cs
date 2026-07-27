namespace Sentory.Platform.Windows.Interop;

public sealed record KakaoDropTarget(
    nint ChatRootWindow,
    nint InputWindow,
    uint ProcessId,
    WindowBounds ChatBounds,
    WindowBounds InputBounds);

public sealed class KakaoDropTargetLocator(
    INativeWindowApi native,
    IKakaoDropWindowApi dropWindows)
{
    public KakaoDropTarget? FindAt(
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
                    KakaoContextValidator.KakaoProcessName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var chatBounds = native.GetWindowBounds(root);
            if (!Contains(chatBounds, cursorX, cursorY) ||
                chatBounds.Width < 320 ||
                chatBounds.Height < 320)
            {
                continue;
            }

            if (requireTopmost &&
                native.GetRootWindow(
                    dropWindows.GetWindowAtPoint(cursorX, cursorY)) != root)
            {
                continue;
            }

            var input = dropWindows.FindDescendant(
                root,
                KakaoContextValidator.InputClassName,
                KakaoContextValidator.InputControlId);
            if (input == nint.Zero ||
                native.GetRootWindow(input) != root ||
                native.GetProcessId(input) != processId ||
                !native.HasDescendant(
                    root,
                    KakaoContextValidator.MessageListClassName,
                    KakaoContextValidator.MessageListControlId))
            {
                continue;
            }

            var inputBounds = native.GetWindowBounds(input);
            if (inputBounds.Width < 120 || inputBounds.Height < 44)
            {
                continue;
            }

            return new KakaoDropTarget(
                root,
                input,
                processId,
                chatBounds,
                inputBounds);
        }

        return null;
    }

    public bool IsWithinTargetBounds(
        KakaoDropTarget target,
        int cursorX,
        int cursorY)
        => Contains(target.ChatBounds, cursorX, cursorY);

    private static bool Contains(
        WindowBounds bounds,
        int x,
        int y) =>
        x >= bounds.Left &&
        x < bounds.Right &&
        y >= bounds.Top &&
        y < bounds.Bottom;
}
