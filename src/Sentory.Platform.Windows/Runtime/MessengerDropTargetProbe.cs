using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal static class MessengerDropTargetProbe
{
    public static bool IsProcessAt(
        INativeWindowApi native,
        IKakaoDropWindowApi dropWindows,
        (int X, int Y) cursor,
        string expectedProcessName) =>
        IsProcessAt(
            native,
            dropWindows,
            cursor,
            processName => string.Equals(
                processName,
                expectedProcessName,
                StringComparison.OrdinalIgnoreCase));

    public static bool IsProcessAt(
        INativeWindowApi native,
        IKakaoDropWindowApi dropWindows,
        (int X, int Y) cursor,
        Func<string?, bool> isSupportedProcess)
    {
        var window = dropWindows.GetWindowAtPoint(cursor.X, cursor.Y);
        var root = native.GetRootWindow(window);
        return isSupportedProcess(
            native.GetProcessName(native.GetProcessId(root)));
    }
}
