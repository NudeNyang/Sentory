namespace Sentory.Platform.Windows.Runtime;

public sealed class ExplorerFileDragActivationState(
    double minimumDragDistance = 8)
{
    private bool _leftWasDown;
    private bool _explorerDragCandidate;
    private bool _rejectedUntilRelease;
    private bool _hasPointerUpSample;
    private bool _lastPointerUpWasExplorer;
    private (int X, int Y) _lastPointerUpPosition;
    private (int X, int Y) _dragStart;

    public bool Observe(
        bool leftDown,
        (int X, int Y) cursor,
        Func<bool> isExplorerAtPointer)
    {
        if (!leftDown)
        {
            _leftWasDown = false;
            _explorerDragCandidate = false;
            _rejectedUntilRelease = false;
            if (!_hasPointerUpSample ||
                _lastPointerUpPosition != cursor)
            {
                _lastPointerUpWasExplorer = isExplorerAtPointer();
            }

            _hasPointerUpSample = true;
            _lastPointerUpPosition = cursor;
            return false;
        }

        if (_rejectedUntilRelease)
        {
            return false;
        }

        if (!_leftWasDown)
        {
            _leftWasDown = true;
            _explorerDragCandidate = isExplorerAtPointer() ||
                                     (_hasPointerUpSample &&
                                      _lastPointerUpWasExplorer);
            _dragStart = _hasPointerUpSample
                ? _lastPointerUpPosition
                : cursor;
        }

        return _explorerDragCandidate &&
               DistanceFromStart(cursor) >= minimumDragDistance;
    }

    public void ResetActiveDrag()
    {
        _leftWasDown = false;
        _explorerDragCandidate = false;
        _rejectedUntilRelease = false;
    }

    public void RejectActiveDragUntilRelease()
    {
        _explorerDragCandidate = false;
        _rejectedUntilRelease = true;
    }

    private double DistanceFromStart((int X, int Y) cursor)
    {
        var x = cursor.X - _dragStart.X;
        var y = cursor.Y - _dragStart.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}
