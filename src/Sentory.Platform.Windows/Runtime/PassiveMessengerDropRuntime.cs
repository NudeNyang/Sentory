using System.Windows.Threading;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal sealed class PassiveMessengerDropRuntime<TTarget> : IDisposable
    where TTarget : class
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan ExplorerOriginMaximumAge =
        TimeSpan.FromMilliseconds(150);

    private readonly INativeWindowApi _native;
    private readonly IKakaoDropWindowApi _dropWindows;
    private readonly IExplorerSelectionReader _selectionReader;
    private readonly Func<bool> _isPaused;
    private readonly Func<string?, bool> _isTargetProcess;
    private readonly Func<int, int, TTarget?> _findTarget;
    private readonly Func<
        TTarget,
        IReadOnlyList<string>,
        Task<string>> _registerDrop;
    private readonly Func<TTarget, string> _targetDescription;
    private readonly Action<string, string>? _diagnostic;
    private readonly string _diagnosticPrefix;
    private readonly DispatcherTimer _timer;
    private readonly PassiveMessengerDropState<TTarget> _dropState;
    private readonly RecentExplorerDragOrigin _dragOrigin =
        new(ExplorerOriginMaximumAge);
    private bool _started;
    private bool _leftWasDown;
    private long _lastSharedDragGeneration;

    public PassiveMessengerDropRuntime(
        INativeWindowApi native,
        IKakaoDropWindowApi dropWindows,
        IExplorerSelectionReader selectionReader,
        Func<bool> isPaused,
        Func<string?, bool> isTargetProcess,
        Func<int, int, TTarget?> findTarget,
        Func<
            TTarget,
            IReadOnlyList<string>,
            Task<string>> registerDrop,
        Func<TTarget, WindowBounds> boundsSelector,
        Func<TTarget, string> targetDescription,
        string diagnosticPrefix,
        Action<string, string>? diagnostic)
    {
        _native = native;
        _dropWindows = dropWindows;
        _selectionReader = selectionReader;
        _isPaused = isPaused;
        _isTargetProcess = isTargetProcess;
        _findTarget = findTarget;
        _registerDrop = registerDrop;
        _targetDescription = targetDescription;
        _diagnosticPrefix = diagnosticPrefix;
        _diagnostic = diagnostic;
        _dropState = new PassiveMessengerDropState<TTarget>(boundsSelector);
        _timer = new DispatcherTimer(
            PollInterval,
            DispatcherPriority.Input,
            OnTick,
            Dispatcher.CurrentDispatcher);
        _timer.Stop();
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _timer.Start();
        _diagnostic?.Invoke(
            $"{_diagnosticPrefix}-drop-runtime-started",
            $"paused={_isPaused()}, timerEnabled={_timer.IsEnabled}");
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_isPaused())
        {
            ResetDrag();
            return;
        }

        var leftDown = _dropWindows.IsLeftMouseButtonDown();
        var cursor = _dropWindows.GetCursorPosition();
        if (_leftWasDown && _dropWindows.IsEscapeKeyDown())
        {
            SharedExplorerImageDragSession.Current.Clear();
            ResetDrag();
            _diagnostic?.Invoke(
                $"{_diagnosticPrefix}-drop-cancelled",
                "reason=escape");
            return;
        }

        if (!leftDown)
        {
            if (!_dropState.IsTracking &&
                JoinSharedExplorerDrag(includeEnded: true))
            {
                _leftWasDown = true;
            }

            CompleteDrag(cursor);
            RememberPointerUp(cursor);
            return;
        }

        if (!_leftWasDown)
        {
            _leftWasDown = true;
            BeginPossibleExplorerDrag(cursor);
        }

        if (!_dropState.IsTracking)
        {
            JoinSharedExplorerDrag();
        }

        if (_dropState.IsTracking)
        {
            _dropState.Observe(
                cursor,
                FindTargetAt(cursor));
        }
    }

    private void BeginPossibleExplorerDrag((int X, int Y) cursor)
    {
        if (JoinSharedExplorerDrag())
        {
            return;
        }

        var explorer = FindExplorerAt(cursor);
        var start = cursor;
        if (explorer == nint.Zero &&
            _dragOrigin.TryGet(
                DateTimeOffset.UtcNow,
                out var recentExplorer,
                out var recentPosition))
        {
            explorer = recentExplorer;
            start = recentPosition;
        }

        if (explorer == nint.Zero)
        {
            return;
        }

        var paths = _selectionReader
            .ReadSelectedFiles(explorer)
            .Where(ClipboardImageCodec.IsSupportedImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        _lastSharedDragGeneration =
            SharedExplorerImageDragSession.Current.Publish(
            start,
            paths,
            DateTimeOffset.UtcNow);
        _dropState.Begin(start, paths);
        _diagnostic?.Invoke(
            $"{_diagnosticPrefix}-drop-selection-observed",
            $"files={paths.Length}");
    }

    private void CompleteDrag((int X, int Y) cursor)
    {
        if (!_leftWasDown)
        {
            return;
        }

        _leftWasDown = false;
        SharedExplorerImageDragSession.Current.End(
            _lastSharedDragGeneration);
        if (!_dropState.IsTracking)
        {
            return;
        }

        _dropState.Observe(
            cursor,
            FindTargetAt(cursor));
        if (_dropState.TryTakeCompleted(
                out var target,
                out var paths))
        {
            _ = RegisterPassiveDropAsync(target, paths);
        }
    }

    private bool JoinSharedExplorerDrag(bool includeEnded = false)
    {
        var shared = SharedExplorerImageDragSession.Current;
        var found = includeEnded
            ? shared.TryGetRecent(
                DateTimeOffset.UtcNow,
                out var generation,
                out var start,
                out var paths)
            : shared.TryGet(
                DateTimeOffset.UtcNow,
                out generation,
                out start,
                out paths);
        if (!found || generation == _lastSharedDragGeneration)
        {
            return false;
        }

        _lastSharedDragGeneration = generation;
        _dropState.Begin(start, paths);
        _diagnostic?.Invoke(
            $"{_diagnosticPrefix}-drop-selection-observed",
            $"files={paths.Count}, source={(includeEnded ? "shared-release" : "shared")}");
        return true;
    }

    private TTarget? FindTargetAt((int X, int Y) cursor)
    {
        var window = _dropWindows.GetWindowAtPoint(cursor.X, cursor.Y);
        var root = _native.GetRootWindow(window);
        var processName = _native.GetProcessName(
            _native.GetProcessId(root));
        return _isTargetProcess(processName)
            ? _findTarget(cursor.X, cursor.Y)
            : null;
    }

    private void RememberPointerUp((int X, int Y) cursor)
    {
        _dragOrigin.Observe(
            cursor,
            FindExplorerAt(cursor),
            DateTimeOffset.UtcNow);
    }

    private nint FindExplorerAt((int X, int Y) cursor)
    {
        var window = _dropWindows.GetWindowAtPoint(cursor.X, cursor.Y);
        var root = _native.GetRootWindow(window);
        return string.Equals(
            _native.GetProcessName(_native.GetProcessId(root)),
            "explorer",
            StringComparison.OrdinalIgnoreCase)
            ? root
            : nint.Zero;
    }

    private async Task RegisterPassiveDropAsync(
        TTarget target,
        IReadOnlyList<string> paths)
    {
        var description = _targetDescription(target);
        _diagnostic?.Invoke(
            $"{_diagnosticPrefix}-drop-released",
            $"files={paths.Count}, {description}, mode=passive");
        var result = await _registerDrop(target, paths);
        _diagnostic?.Invoke(
            $"{_diagnosticPrefix}-drop-result",
            $"result={result}, files={paths.Count}, mode=passive");
    }

    private void ResetDrag()
    {
        _leftWasDown = false;
        _dropState.Reset();
    }

    public void Dispose()
    {
        _timer.Stop();
        _dropState.Reset();
    }
}
