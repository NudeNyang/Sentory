using System.Windows.Threading;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public sealed class LineDropOverlayRuntime : IDisposable
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan ExplorerOriginMaximumAge =
        TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan IdleBaselineRefreshInterval =
        TimeSpan.FromSeconds(2);
    private const int ReleaseTargetGraceFrames = 48;

    private readonly INativeWindowApi _native;
    private readonly IKakaoDropWindowApi _dropWindows;
    private readonly LineDropTargetLocator _locator;
    private readonly IExplorerSelectionReader _selectionReader;
    private readonly LineCaptureRuntime _captureRuntime;
    private readonly Action<string, string>? _diagnostic;
    private readonly DispatcherTimer _timer;
    private readonly LinePassiveDropState _dropState = new();
    private readonly RecentExplorerDragOrigin _dragOrigin =
        new(ExplorerOriginMaximumAge);
    private bool _started;
    private bool _leftWasDown;
    private long _lastSharedDragGeneration;
    private int _releaseTargetGraceFrames;
    private DateTimeOffset _releasedAt;
    private nint _preDropBaselineWindow;
    private Task<LineAccessibilitySnapshot?>? _preDropBaselineTask;
    private Task? _idleBaselineRefreshTask;
    private DateTimeOffset _nextIdleBaselineRefreshAt;

    public LineDropOverlayRuntime(
        LineCaptureRuntime captureRuntime,
        Action<string, string>? diagnostic = null)
        : this(
            captureRuntime,
            new NativeWindowApi(),
            new ExplorerSelectionReader(),
            diagnostic)
    {
    }

    internal LineDropOverlayRuntime(
        LineCaptureRuntime captureRuntime,
        NativeWindowApi native,
        IExplorerSelectionReader selectionReader,
        Action<string, string>? diagnostic = null)
    {
        _native = native;
        _dropWindows = native;
        _locator = new LineDropTargetLocator(native, native);
        _selectionReader = selectionReader;
        _captureRuntime = captureRuntime;
        _diagnostic = diagnostic;
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
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_captureRuntime.IsPaused)
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
            _diagnostic?.Invoke("line-drop-cancelled", "reason=escape");
            return;
        }

        if (!leftDown)
        {
            if (_releaseTargetGraceFrames <= 0 &&
                !_dropState.IsTracking &&
                TryJoinSharedExplorerDrag(includeEnded: true))
            {
                _leftWasDown = true;
            }

            CompleteDrag(cursor);
            RememberPointerUp(cursor);
            BeginIdleBaselineRefresh();
            return;
        }

        if (!_leftWasDown && _releaseTargetGraceFrames > 0)
        {
            _diagnostic?.Invoke(
                "line-drop-cancelled",
                "reason=next-drag-started-during-release-grace");
            ResetDrag();
        }

        if (!_leftWasDown)
        {
            _leftWasDown = true;
            BeginPossibleExplorerDrag(cursor);
        }

        if (!_dropState.IsTracking)
        {
            TryJoinSharedExplorerDrag();
        }

        if (_dropState.IsTracking)
        {
            var target = MessengerDropTargetProbe.IsProcessAt(
                    _native,
                    _dropWindows,
                    cursor,
                    LineContextValidator.ProcessName)
                ? _locator.FindAt(
                        cursor.X,
                        cursor.Y,
                        requireTopmost: true)
                : null;
            _dropState.Observe(cursor, target);
            if (target is not null)
            {
                BeginPreDropBaselineCapture(target);
            }
        }
    }

    private void BeginPossibleExplorerDrag((int X, int Y) cursor)
    {
        if (TryJoinSharedExplorerDrag())
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
        BeginPreDropBaselineCaptureFromVisibleWindow();
        _diagnostic?.Invoke(
            "line-drop-selection-observed",
            $"files={paths.Length}");
    }

    private bool TryJoinSharedExplorerDrag(bool includeEnded = false)
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
        BeginPreDropBaselineCaptureFromVisibleWindow();
        _diagnostic?.Invoke(
            "line-drop-selection-observed",
            $"files={paths.Count}, source={(includeEnded ? "shared-release" : "shared")}");
        return true;
    }

    private void CompleteDrag((int X, int Y) cursor)
    {
        if (_leftWasDown)
        {
            _leftWasDown = false;
            SharedExplorerImageDragSession.Current.End(
                _lastSharedDragGeneration,
                DateTimeOffset.UtcNow);
            _releaseTargetGraceFrames = ReleaseTargetGraceFrames;
            _releasedAt = DateTimeOffset.UtcNow;
        }

        if (_releaseTargetGraceFrames <= 0)
        {
            return;
        }

        if (!_dropState.IsTracking)
        {
            _releaseTargetGraceFrames = 0;
            return;
        }

        _dropState.Observe(
            cursor,
            _locator.FindTrackedReleaseAt(cursor.X, cursor.Y));
        if (_dropState.TryTakeCompleted(out var target, out var paths))
        {
            var releasedAt = _releasedAt;
            var preDropBaselineTask = _preDropBaselineTask;
            _releaseTargetGraceFrames = 0;
            _releasedAt = default;
            _preDropBaselineWindow = nint.Zero;
            _preDropBaselineTask = null;
            _ = RegisterPassiveDropAsync(
                target,
                paths,
                releasedAt,
                preDropBaselineTask);
            return;
        }

        _releaseTargetGraceFrames--;
        if (_releaseTargetGraceFrames == 0)
        {
            _dropState.Reset();
            _diagnostic?.Invoke(
                "line-drop-cancelled",
                "reason=release-target-unavailable");
        }
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
        LineDropTarget target,
        IReadOnlyList<string> paths,
        DateTimeOffset releasedAt,
        Task<LineAccessibilitySnapshot?>? preDropBaselineTask)
    {
        _diagnostic?.Invoke(
            "line-drop-released",
            $"files={paths.Count}, window=0x{target.MainWindow.ToInt64():X}, mode=passive");
        var preDropBaseline =
            LinePreDropBaselinePolicy.TryGetCompleted(preDropBaselineTask);
        if (preDropBaselineTask is { IsCompleted: false })
        {
            _diagnostic?.Invoke(
                "line-drop-baseline-pending",
                "registration=immediate");
        }
        var result = await _captureRuntime.RegisterNativeDroppedFilesAsync(
            target,
            paths,
            releasedAt,
            preDropBaseline);
        _diagnostic?.Invoke(
            "line-drop-result",
            $"result={result}, files={paths.Count}, mode=passive");
    }

    private void ResetDrag()
    {
        _leftWasDown = false;
        _releaseTargetGraceFrames = 0;
        _releasedAt = default;
        _preDropBaselineWindow = nint.Zero;
        _preDropBaselineTask = null;
        _dropState.Reset();
    }

    private void BeginPreDropBaselineCapture(LineDropTarget target)
    {
        if (_preDropBaselineTask is not null &&
            _preDropBaselineWindow == target.MainWindow)
        {
            return;
        }

        _preDropBaselineWindow = target.MainWindow;
        _preDropBaselineTask =
            _captureRuntime.TryCaptureNativeDropBaselineAsync(
                target,
                DateTimeOffset.UtcNow);
    }

    private void BeginPreDropBaselineCaptureFromVisibleWindow()
    {
        var target = _locator.FindVisibleMainWindow();
        if (target is not null)
        {
            _dropState.RememberFallbackTarget(target);
            BeginPreDropBaselineCapture(target);
        }
    }

    private void BeginIdleBaselineRefresh()
    {
        if (_dropState.IsTracking ||
            _releaseTargetGraceFrames > 0 ||
            _idleBaselineRefreshTask is { IsCompleted: false } ||
            DateTimeOffset.UtcNow < _nextIdleBaselineRefreshAt)
        {
            return;
        }

        var target = _locator.FindVisibleMainWindow();
        if (target is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _nextIdleBaselineRefreshAt = now + IdleBaselineRefreshInterval;
        _idleBaselineRefreshTask =
            _captureRuntime.RefreshNativeDropBaselineAsync(target, now);
    }

    public void Dispose()
    {
        _timer.Stop();
        _dropState.Reset();
        GC.SuppressFinalize(this);
    }
}
