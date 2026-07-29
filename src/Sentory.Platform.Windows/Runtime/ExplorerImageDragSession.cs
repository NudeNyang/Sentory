namespace Sentory.Platform.Windows.Runtime;

internal sealed class ExplorerImageDragSession(TimeSpan maximumAge)
{
    private readonly object _gate = new();
    private (int X, int Y) _start;
    private string[] _paths = [];
    private DateTimeOffset _observedAt;
    private long _generation;
    private bool _active;

    public long Publish(
        (int X, int Y) start,
        IReadOnlyList<string> paths,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (_gate)
        {
            _generation++;
            _start = start;
            _paths = paths.ToArray();
            _observedAt = observedAt;
            _active = true;
            return _generation;
        }
    }

    public bool TryGet(
        DateTimeOffset now,
        out long generation,
        out (int X, int Y) start,
        out IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            generation = default;
            start = default;
            paths = [];
            if (!_active || !IsRecent(now))
            {
                return false;
            }

            generation = _generation;
            start = _start;
            paths = _paths;
            return true;
        }
    }

    public bool TryGetRecent(
        DateTimeOffset now,
        out long generation,
        out (int X, int Y) start,
        out IReadOnlyList<string> paths)
    {
        lock (_gate)
        {
            generation = default;
            start = default;
            paths = [];
            if (!IsRecent(now))
            {
                return false;
            }

            generation = _generation;
            start = _start;
            paths = _paths;
            return true;
        }
    }

    public void End(long generation)
    {
        lock (_gate)
        {
            if (_generation == generation)
            {
                _active = false;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _active = false;
            _paths = [];
            _observedAt = default;
        }
    }

    private bool IsRecent(DateTimeOffset now)
    {
        return _paths.Length > 0 &&
               now >= _observedAt &&
               now - _observedAt <= maximumAge;
    }
}

internal static class SharedExplorerImageDragSession
{
    public static ExplorerImageDragSession Current { get; } =
        new(TimeSpan.FromMilliseconds(500));
}
