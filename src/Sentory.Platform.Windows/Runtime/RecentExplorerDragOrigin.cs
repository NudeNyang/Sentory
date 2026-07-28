namespace Sentory.Platform.Windows.Runtime;

internal sealed class RecentExplorerDragOrigin(
    TimeSpan maximumAge)
{
    private (int X, int Y) _position;
    private nint _explorerWindow;
    private DateTimeOffset _observedAt;

    public void Observe(
        (int X, int Y) position,
        nint explorerWindow,
        DateTimeOffset observedAt)
    {
        _position = position;
        _explorerWindow = explorerWindow;
        _observedAt = observedAt;
    }

    public bool TryGet(
        DateTimeOffset now,
        out nint explorerWindow,
        out (int X, int Y) position)
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
