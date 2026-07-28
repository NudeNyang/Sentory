using System.Windows.Threading;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public sealed class TelegramDropOverlayRuntime : IDisposable
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan ExplorerOriginMaximumAge =
        TimeSpan.FromMilliseconds(150);
    private const int ReleaseTargetGraceFrames = 16;

    private readonly INativeWindowApi _native;
    private readonly IKakaoDropWindowApi _dropWindows;
    private readonly TelegramDropTargetLocator _locator;
    private readonly IExplorerSelectionReader _selectionReader;
    private readonly TelegramCaptureRuntime _captureRuntime;
    private readonly Action<string, string>? _diagnostic;
    private readonly DispatcherTimer _timer;
    private readonly TelegramPassiveDropState _dropState = new();
    private readonly RecentExplorerDragOrigin _dragOrigin =
        new(ExplorerOriginMaximumAge);
    private bool _started;
    private bool _leftWasDown;
    private int _releaseTargetGraceFrames;
    private DateTimeOffset _releasedAt;

    public TelegramDropOverlayRuntime(
        TelegramCaptureRuntime captureRuntime,
        Action<string, string>? diagnostic = null)
        : this(
            captureRuntime,
            new NativeWindowApi(),
            new ExplorerSelectionReader(),
            diagnostic)
    {
    }

    internal TelegramDropOverlayRuntime(
        TelegramCaptureRuntime captureRuntime,
        NativeWindowApi native,
        IExplorerSelectionReader selectionReader,
        Action<string, string>? diagnostic = null)
    {
        _native = native;
        _dropWindows = native;
        _locator = new TelegramDropTargetLocator(native, native);
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
            _diagnostic?.Invoke("telegram-drop-cancelled", "reason=escape");
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

        if (_dropState.IsTracking)
        {
            _dropState.Observe(
                cursor,
                _locator.FindAt(cursor.X, cursor.Y, requireTopmost: true));
        }
    }

    private void BeginPossibleExplorerDrag((int X, int Y) cursor)
    {
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

        _dropState.Begin(start, paths);
        _diagnostic?.Invoke(
            "telegram-drop-selection-observed",
            $"files={paths.Length}");
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
            return;
        }

        _dropState.Observe(
            cursor,
            _locator.FindAt(cursor.X, cursor.Y, requireTopmost: true));
        if (_dropState.TryTakeCompleted(out var target, out var paths))
        {
            var releasedAt = _releasedAt;
            _releaseTargetGraceFrames = 0;
            _releasedAt = default;
            _ = RegisterPassiveDropAsync(target, paths, releasedAt);
            return;
        }

        _releaseTargetGraceFrames--;
        if (_releaseTargetGraceFrames == 0)
        {
            _dropState.Reset();
            _diagnostic?.Invoke(
                "telegram-drop-cancelled",
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
        TelegramDropTarget target,
        IReadOnlyList<string> paths,
        DateTimeOffset releasedAt)
    {
        _diagnostic?.Invoke(
            "telegram-drop-released",
            $"files={paths.Count}, window=0x{target.MainWindow.ToInt64():X}, mode=passive");
        var result = await _captureRuntime.RegisterNativeDroppedFilesAsync(
            target,
            paths,
            releasedAt);
        _diagnostic?.Invoke(
            "telegram-drop-result",
            $"result={result}, files={paths.Count}, mode=passive");
    }

    private void ResetDrag()
    {
        _leftWasDown = false;
        _releaseTargetGraceFrames = 0;
        _releasedAt = default;
        _dropState.Reset();
    }

    public void Dispose()
    {
        _timer.Stop();
        _dropState.Reset();
        GC.SuppressFinalize(this);
    }
}
