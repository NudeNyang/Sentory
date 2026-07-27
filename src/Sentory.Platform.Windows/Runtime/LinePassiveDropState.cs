using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal sealed class LinePassiveDropState(
    double minimumDragDistance = 8)
{
    private (int X, int Y) _start;
    private string[] _paths = [];
    private LineDropTarget? _target;
    private bool _activated;

    public bool IsTracking => _paths.Length > 0;

    public void Begin(
        (int X, int Y) start,
        IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Reset();
        _start = start;
        _paths = paths.ToArray();
    }

    public void Observe(
        (int X, int Y) cursor,
        LineDropTarget? target)
    {
        if (!IsTracking)
        {
            return;
        }

        var x = cursor.X - _start.X;
        var y = cursor.Y - _start.Y;
        _activated |= Math.Sqrt((x * x) + (y * y)) >=
                      minimumDragDistance;
        _target = _activated ? target : null;
    }

    public bool TryTakeCompleted(
        out LineDropTarget target,
        out IReadOnlyList<string> paths)
    {
        target = null!;
        paths = [];
        if (!_activated || _target is null)
        {
            Reset();
            return false;
        }

        target = _target;
        paths = _paths;
        Reset();
        return true;
    }

    public void Reset()
    {
        _paths = [];
        _target = null;
        _activated = false;
    }
}
