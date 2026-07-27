using System.Windows.Threading;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public sealed class LineDropOverlayRuntime : IDisposable
{
    private const double CachedExplorerMaximumDistance = 64;
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(16);

    private readonly INativeWindowApi _native;
    private readonly IKakaoDropWindowApi _dropWindows;
    private readonly LineDropTargetLocator _locator;
    private readonly IExplorerSelectionReader _selectionReader;
    private readonly LineCaptureRuntime _captureRuntime;
    private readonly Action<string, string>? _diagnostic;
    private readonly DispatcherTimer _timer;
    private readonly LinePassiveDropState _dropState = new();
    private bool _started;
    private bool _leftWasDown;
    private bool _hasPointerUpSample;
    private (int X, int Y) _lastPointerUpPosition;
    private nint _lastPointerUpExplorer;

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
            ResetDrag();
            _diagnostic?.Invoke("line-drop-cancelled", "reason=escape");
            return;
        }

        if (!leftDown)
        {
            CompleteDrag(cursor);
            RememberPointerUp(cursor);
            return;
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
                _locator.FindAt(
                    cursor.X,
                    cursor.Y,
                    requireTopmost: true));
        }
    }

    private void BeginPossibleExplorerDrag((int X, int Y) cursor)
    {
        var explorer = FindExplorerAt(cursor);
        var start = cursor;
        if (explorer == nint.Zero &&
            _hasPointerUpSample &&
            Distance(_lastPointerUpPosition, cursor) <=
            CachedExplorerMaximumDistance)
        {
            explorer = _lastPointerUpExplorer;
            start = _lastPointerUpPosition;
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
            "line-drop-selection-observed",
            $"files={paths.Length}");
    }

    private void CompleteDrag((int X, int Y) cursor)
    {
        if (!_leftWasDown)
        {
            return;
        }

        _leftWasDown = false;
        if (!_dropState.IsTracking)
        {
            return;
        }

        _dropState.Observe(
            cursor,
            _locator.FindAt(
                cursor.X,
                cursor.Y,
                requireTopmost: true));
        if (_dropState.TryTakeCompleted(out var target, out var paths))
        {
            _ = RegisterPassiveDropAsync(target, paths);
        }
    }

    private void RememberPointerUp((int X, int Y) cursor)
    {
        _hasPointerUpSample = true;
        _lastPointerUpPosition = cursor;
        _lastPointerUpExplorer = FindExplorerAt(cursor);
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

    private static double Distance(
        (int X, int Y) left,
        (int X, int Y) right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    private async Task RegisterPassiveDropAsync(
        LineDropTarget target,
        IReadOnlyList<string> paths)
    {
        _diagnostic?.Invoke(
            "line-drop-released",
            $"files={paths.Count}, window=0x{target.MainWindow.ToInt64():X}, mode=passive");
        var result = await _captureRuntime.RegisterNativeDroppedFilesAsync(
            target,
            paths);
        _diagnostic?.Invoke(
            "line-drop-result",
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
        GC.SuppressFinalize(this);
    }
}
