using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal sealed class RecentExplorerDragOrigin(
    TimeSpan maximumAge)
{
    private readonly object _gate = new();
    private (int X, int Y) _position;
    private nint _explorerWindow;
    private DateTimeOffset _observedAt;

    public void Observe(
        (int X, int Y) position,
        nint explorerWindow,
        DateTimeOffset observedAt)
    {
        lock (_gate)
        {
            _position = position;
            _explorerWindow = explorerWindow;
            _observedAt = observedAt;
        }
    }

    public bool TryGet(
        DateTimeOffset now,
        out nint explorerWindow,
        out (int X, int Y) position)
    {
        lock (_gate)
        {
            explorerWindow = nint.Zero;
            position = default;
            if (_explorerWindow == nint.Zero ||
                now < _observedAt ||
                now - _observedAt > maximumAge)
            {
                return false;
            }

            explorerWindow = _explorerWindow;
            position = _position;
            return true;
        }
    }
}

internal static class ExplorerPointerDownOriginTracker
{
    private static readonly RecentExplorerDragOrigin SharedOrigin =
        new(TimeSpan.FromSeconds(2));

    public static void ObserveShared(
        INativeWindowApi native,
        PointerTrigger trigger) =>
        Observe(SharedOrigin, native, trigger);

    public static bool TryGetShared(
        DateTimeOffset now,
        out nint explorerWindow,
        out (int X, int Y) position) =>
        SharedOrigin.TryGet(now, out explorerWindow, out position);

    internal static void Observe(
        RecentExplorerDragOrigin state,
        INativeWindowApi native,
        PointerTrigger trigger)
    {
        var window = trigger.ForegroundWindow;
        if (native is IKakaoDropWindowApi pointerWindows)
        {
            var pointWindow = pointerWindows.GetWindowAtPoint(
                trigger.ScreenX,
                trigger.ScreenY);
            if (pointWindow != nint.Zero)
            {
                window = pointWindow;
            }
        }

        var root = native.GetRootWindow(window);
        var processName = root == nint.Zero
            ? null
            : native.GetProcessName(native.GetProcessId(root));
        state.Observe(
            (trigger.ScreenX, trigger.ScreenY),
            string.Equals(
                processName,
                "explorer",
                StringComparison.OrdinalIgnoreCase)
                ? root
                : nint.Zero,
            trigger.OccurredAt);
    }
}
