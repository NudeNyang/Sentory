using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal sealed class PassiveMessengerDropState<TTarget>(
    Func<TTarget, WindowBounds> boundsSelector,
    double minimumDragDistance = 8)
    where TTarget : class
{
    private const int MaximumTargetMissFrames = 3;

    private (int X, int Y) _start;
    private string[] _paths = [];
    private TTarget? _target;
    private bool _activated;
    private int _targetMissFrames;

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
        TTarget? target)
    {
        if (!IsTracking)
        {
            return;
        }

        var x = cursor.X - _start.X;
        var y = cursor.Y - _start.Y;
        _activated |= Math.Sqrt((x * x) + (y * y)) >=
                      minimumDragDistance;
        if (!_activated)
        {
            return;
        }

        if (target is not null)
        {
            _target = target;
            _targetMissFrames = 0;
            return;
        }

        if (_target is null ||
            !Contains(boundsSelector(_target), cursor))
        {
            _target = null;
            _targetMissFrames = 0;
            return;
        }

        _targetMissFrames++;
        if (_targetMissFrames > MaximumTargetMissFrames)
        {
            _target = null;
            _targetMissFrames = 0;
        }
    }

    public bool TryTakeCompleted(
        out TTarget target,
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
        _targetMissFrames = 0;
    }

    private static bool Contains(
        WindowBounds bounds,
        (int X, int Y) cursor) =>
        cursor.X >= bounds.Left &&
        cursor.X < bounds.Right &&
        cursor.Y >= bounds.Top &&
        cursor.Y < bounds.Bottom;
}
