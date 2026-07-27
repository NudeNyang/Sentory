using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Sentory.Platform.Windows.Interop;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;

namespace Sentory.Platform.Windows.Runtime;

public sealed class SlackDropOverlayRuntime : IDisposable
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(16);

    private readonly INativeWindowApi _native;
    private readonly IKakaoDropWindowApi _dropWindows;
    private readonly SlackDropTargetLocator _locator;
    private readonly SlackCaptureRuntime _captureRuntime;
    private readonly Action<string, string>? _diagnostic;
    private readonly DispatcherTimer _timer;
    private readonly Window _window;
    private readonly SlackDropPassThroughState _passThrough = new();
    private readonly ExplorerFileDragActivationState _dragActivation = new();
    private bool _started;
    private bool _oleDragOver;
    private SlackDropTarget? _currentTarget;

    public SlackDropOverlayRuntime(
        SlackCaptureRuntime captureRuntime,
        Action<string, string>? diagnostic = null)
    {
        var native = new NativeWindowApi();
        _native = native;
        _dropWindows = native;
        _locator = new SlackDropTargetLocator(native, native, native);
        _captureRuntime = captureRuntime;
        _diagnostic = diagnostic;

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = WpfBrushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            AllowDrop = true,
            Content = new Border
            {
                Background = new SolidColorBrush(
                    WpfColor.FromArgb(1, 255, 255, 255)),
                BorderThickness = new Thickness(0)
            },
            SizeToContent = SizeToContent.Manual
        };
        _window.DragEnter += OnDragEnter;
        _window.DragOver += OnDragOver;
        _window.DragLeave += OnDragLeave;
        _window.Drop += OnDrop;

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
            _passThrough.Cancel();
            ResetDrag();
            HideOverlay();
            return;
        }

        var leftDown = _dropWindows.IsLeftMouseButtonDown();
        var cursor = _dropWindows.GetCursorPosition();
        if (_passThrough.IsActive && _dropWindows.IsEscapeKeyDown())
        {
            _passThrough.Cancel();
            ResetDrag();
            _diagnostic?.Invoke("slack-drop-cancelled", "reason=escape");
            return;
        }

        if (_passThrough.TryTakeCompleted(
                leftDown,
                out var completedTarget,
                out var completedPaths))
        {
            ResetDrag();
            _ = RegisterPassedThroughDropAsync(
                completedTarget,
                completedPaths);
            return;
        }

        if (_passThrough.IsActive)
        {
            return;
        }

        var shouldInspectTarget = _dragActivation.Observe(
            leftDown,
            cursor,
            () => IsExplorerAt(cursor));
        if (!leftDown)
        {
            if (_oleDragOver)
            {
                return;
            }

            ResetDrag();
            HideOverlay();
            return;
        }

        if (!shouldInspectTarget)
        {
            HideOverlay();
            return;
        }

        var target = _locator.FindAt(
            cursor.X,
            cursor.Y,
            requireTopmost: !_window.IsVisible);
        if (target is null)
        {
            HideOverlay();
            return;
        }

        ShowOverlay(target);
    }

    private bool IsExplorerAt((int X, int Y) cursor)
    {
        var window = _dropWindows.GetWindowAtPoint(cursor.X, cursor.Y);
        return string.Equals(
            _native.GetProcessName(_native.GetProcessId(window)),
            "explorer",
            StringComparison.OrdinalIgnoreCase);
    }

    private void ShowOverlay(SlackDropTarget target)
    {
        _currentTarget = target;
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        var handle = new WindowInteropHelper(_window).Handle;
        _dropWindows.PositionTopmostWindow(handle, target.Bounds);
    }

    private void OnDragEnter(object sender, WpfDragEventArgs e)
    {
        _oleDragOver = true;
        ObserveAndPassThrough(e);
    }

    private void OnDragOver(object sender, WpfDragEventArgs e)
    {
        if (_passThrough.IsActive)
        {
            return;
        }

        _oleDragOver = true;
        ObserveAndPassThrough(e);
    }

    private void OnDragLeave(object sender, WpfDragEventArgs e) =>
        _oleDragOver = false;

    private void ObserveAndPassThrough(WpfDragEventArgs e)
    {
        var inspection = FileDropCapturePolicy.Inspect(e.Data);
        var target = _currentTarget;
        if (!inspection.ShouldObserve)
        {
            _dragActivation.RejectActiveDragUntilRelease();
            _oleDragOver = false;
            HideOverlay();
            e.Handled = false;
            return;
        }

        if (target is null)
        {
            _oleDragOver = false;
            HideOverlay();
            e.Handled = false;
            return;
        }

        e.Effects = WpfDragDropEffects.Copy;
        e.Handled = true;
        _passThrough.Observe(target, inspection.ImagePaths);
        _diagnostic?.Invoke(
            "slack-drop-observed",
            $"files={inspection.ImagePaths.Count}, window=0x{target.MainWindow.ToInt64():X}");
        _oleDragOver = false;
        _currentTarget = null;
        _window.Hide();
    }

    private void OnDrop(object sender, WpfDragEventArgs e)
    {
        e.Effects = WpfDragDropEffects.None;
        e.Handled = false;
        ResetDrag();
        HideOverlay();
    }

    private async Task RegisterPassedThroughDropAsync(
        SlackDropTarget target,
        IReadOnlyList<string> paths)
    {
        _diagnostic?.Invoke(
            "slack-drop-released",
            $"files={paths.Count}, window=0x{target.MainWindow.ToInt64():X}");
        var result = await _captureRuntime.RegisterNativeDroppedFilesAsync(
            target,
            paths);
        _diagnostic?.Invoke(
            "slack-drop-result",
            $"result={result}, files={paths.Count}");
    }

    private void ResetDrag()
    {
        _dragActivation.ResetActiveDrag();
        _oleDragOver = false;
    }

    private void HideOverlay()
    {
        _currentTarget = null;
        if (_window.IsVisible)
        {
            _window.Hide();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _passThrough.Reset();
        _window.DragEnter -= OnDragEnter;
        _window.DragOver -= OnDragOver;
        _window.DragLeave -= OnDragLeave;
        _window.Drop -= OnDrop;
        _window.Close();
        GC.SuppressFinalize(this);
    }
}
