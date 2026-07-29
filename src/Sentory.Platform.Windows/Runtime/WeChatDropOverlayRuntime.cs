using System.Windows.Threading;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public sealed class WeChatDropOverlayRuntime : IDisposable
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan ExplorerOriginMaximumAge =
        TimeSpan.FromMilliseconds(150);
    private const int ReleaseTargetGraceFrames = 16;

    private readonly INativeWindowApi _native;
    private readonly IKakaoDropWindowApi _dropWindows;
    private readonly WeChatDropTargetLocator _locator;
    private readonly IExplorerSelectionReader _selectionReader;
    private readonly WeChatCaptureRuntime _captureRuntime;
    private readonly Action<string, string>? _diagnostic;
    private readonly DispatcherTimer _timer;
    private readonly WeChatPassiveDropState _dropState = new();
    private readonly WeChatDropPointerHistory _pointerHistory = new();
    private readonly RecentExplorerDragOrigin _dragOrigin =
        new(ExplorerOriginMaximumAge);
    private bool _started;
    private bool _leftWasDown;
    private int _releaseTargetGraceFrames;
    private DateTimeOffset _releasedAt;

    public WeChatDropOverlayRuntime(
        WeChatCaptureRuntime captureRuntime,
        Action<string, string>? diagnostic = null)
        : this(
            captureRuntime,
            new NativeWindowApi(),
            new ExplorerSelectionReader(),
            diagnostic)
    {
    }

    internal WeChatDropOverlayRuntime(
        WeChatCaptureRuntime captureRuntime,
        NativeWindowApi native,
        IExplorerSelectionReader selectionReader,
        Action<string, string>? diagnostic = null)
    {
        _native = native;
        _dropWindows = native;
        _locator = new WeChatDropTargetLocator(native, native);
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
            ResetDrag();
            _diagnostic?.Invoke("wechat-drop-cancelled", "reason=escape");
            return;
        }

        if (!leftDown)
        {
            CompleteDrag(cursor);
            RememberPointerUp(cursor);
            return;
        }

        if (!_leftWasDown && _releaseTargetGraceFrames > 0)
        {
            ResetDrag();
        }

        if (!_leftWasDown)
        {
            _leftWasDown = true;
            BeginPossibleExplorerDrag(cursor);
        }

        _pointerHistory.ObserveDown(cursor);

        if (_dropState.IsTracking)
        {
            _dropState.Observe(
                cursor,
                MessengerDropTargetProbe.IsProcessAt(
                    _native,
                    _dropWindows,
                    cursor,
                    WeChatContextValidator.IsSupportedDropSurfaceProcessName)
                    ? _locator.FindAt(
                        cursor.X,
                        cursor.Y,
                        requireTopmost: true)
                    : null);
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

        SharedExplorerImageDragSession.Current.Publish(
            start,
            paths,
            DateTimeOffset.UtcNow);
        _dropState.Begin(start, paths);
        _diagnostic?.Invoke(
            "wechat-drop-selection-observed",
            $"files={paths.Length}");
    }

    private bool TryJoinSharedExplorerDrag()
    {
        if (!SharedExplorerImageDragSession.Current.TryGet(
                DateTimeOffset.UtcNow,
                out var start,
                out var paths))
        {
            return false;
        }

        _dropState.Begin(start, paths);
        _diagnostic?.Invoke(
            "wechat-drop-selection-observed",
            $"files={paths.Count}, source=shared");
        return true;
    }

    private void CompleteDrag((int X, int Y) cursor)
    {
        if (_leftWasDown)
        {
            _leftWasDown = false;
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
            _pointerHistory.Reset();
            return;
        }

        var releaseCursor = _pointerHistory.ResolveRelease(cursor);
        var releaseTarget = _locator.FindReleaseAt(
            releaseCursor.X,
            releaseCursor.Y);
        if (releaseTarget is null && releaseCursor != cursor)
        {
            releaseCursor = cursor;
            releaseTarget = _locator.FindReleaseAt(cursor.X, cursor.Y);
        }

        _dropState.Observe(releaseCursor, releaseTarget);
        if (_dropState.TryTakeCompleted(
                out var completedTarget,
                out var paths))
        {
            var releasedAt = _releasedAt;
            _releaseTargetGraceFrames = 0;
            _releasedAt = default;
            _pointerHistory.Reset();
            _ = RegisterPassiveDropAsync(
                completedTarget,
                paths,
                releasedAt);
            return;
        }

        _releaseTargetGraceFrames--;
        if (_releaseTargetGraceFrames == 0)
        {
            _dropState.Reset();
            _pointerHistory.Reset();
            _diagnostic?.Invoke(
                "wechat-drop-cancelled",
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
        WeChatDropTarget target,
        IReadOnlyList<string> paths,
        DateTimeOffset releasedAt)
    {
        _diagnostic?.Invoke(
            "wechat-drop-released",
            $"files={paths.Count}, window=0x{target.MainWindow.ToInt64():X}, mode=passive");
        var result = await _captureRuntime.RegisterNativeDroppedFilesAsync(
            target,
            paths,
            releasedAt);
        _diagnostic?.Invoke(
            "wechat-drop-result",
            $"result={result}, files={paths.Count}, mode=passive");
    }

    private void ResetDrag()
    {
        _leftWasDown = false;
        _releaseTargetGraceFrames = 0;
        _releasedAt = default;
        _dropState.Reset();
        _pointerHistory.Reset();
    }

    public void Dispose()
    {
        _timer.Stop();
        _dropState.Reset();
        GC.SuppressFinalize(this);
    }
}
