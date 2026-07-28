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
    private nint _preDropBaselineWindow;
    private Task<TelegramVisualSnapshot?>? _preDropBaselineTask;

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
            var target = MessengerDropTargetProbe.IsProcessAt(
                    _native,
                    _dropWindows,
                    cursor,
                    TelegramContextValidator.ProcessName)
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

        SharedExplorerImageDragSession.Current.Publish(
            start,
            paths,
            DateTimeOffset.UtcNow);
        _dropState.Begin(start, paths);
        _diagnostic?.Invoke(
            "telegram-drop-selection-observed",
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
            "telegram-drop-selection-observed",
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
            return;
        }

        _dropState.Observe(
            cursor,
            _locator.FindAt(cursor.X, cursor.Y, requireTopmost: true));
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
        DateTimeOffset releasedAt,
        Task<TelegramVisualSnapshot?>? preDropBaselineTask)
    {
        _diagnostic?.Invoke(
            "telegram-drop-released",
            $"files={paths.Count}, window=0x{target.MainWindow.ToInt64():X}, mode=passive");
        var preDropSnapshot = preDropBaselineTask is null
            ? null
            : await preDropBaselineTask;
        var result = await _captureRuntime.RegisterNativeDroppedFilesAsync(
            target,
            paths,
            releasedAt,
            preDropSnapshot);
        _diagnostic?.Invoke(
            "telegram-drop-result",
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

    private void BeginPreDropBaselineCapture(TelegramDropTarget target)
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

    public void Dispose()
    {
        _timer.Stop();
        _dropState.Reset();
        GC.SuppressFinalize(this);
    }
}
