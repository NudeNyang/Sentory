namespace Sentory.Platform.Windows.Runtime;

internal sealed class ExplorerImageDragSession(TimeSpan maximumAge)
{
    private readonly object _gate = new();
    private (int X, int Y) _start;
    private string[] _paths = [];
    private DateTimeOffset _observedAt;

    public void Publish(
        (int X, int Y) start,
        IReadOnlyList<string> paths,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (_gate)
        {
            _start = start;
            _paths = paths.ToArray();
            _observedAt = observedAt;
        }
    }

    public bool TryGet(
        DateTimeOffset now,
        out (int X, int Y) start,
        out IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            start = default;
            paths = [];
            if (_paths.Length == 0 ||
                now < _observedAt ||
                now - _observedAt > maximumAge)
            {
                return false;
            }

            start = _start;
            paths = _paths;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _paths = [];
            _observedAt = default;
        }
    }
}

internal static class SharedExplorerImageDragSession
{
    public static ExplorerImageDragSession Current { get; } =
        new(TimeSpan.FromMilliseconds(500));
}
