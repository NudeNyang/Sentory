using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Sentory.Platform.Windows.Interop;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.IDataObject;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;

namespace Sentory.Platform.Windows.Runtime;

public sealed class DiscordDropOverlayRuntime : IDisposable
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(16);

    private readonly INativeWindowApi _native;
    private readonly IKakaoDropWindowApi _dropWindows;
    private readonly DiscordDropTargetLocator _locator;
    private readonly DiscordCaptureRuntime _captureRuntime;
    private readonly Action<string, string>? _diagnostic;
    private readonly DispatcherTimer _timer;
    private readonly Window _window;
    private readonly DiscordDropPassThroughState _passThrough = new();
    private readonly ExplorerFileDragActivationState _dragActivation = new();
    private bool _started;
    private bool _oleDragOver;
    private DiscordDropTarget? _currentTarget;

    public DiscordDropOverlayRuntime(
        DiscordCaptureRuntime captureRuntime,
        Action<string, string>? diagnostic = null)
    {
        var native = new NativeWindowApi();
        _native = native;
        _dropWindows = native;
        _locator = new DiscordDropTargetLocator(native, native, native);
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
            _diagnostic?.Invoke(
                "discord-drop-cancelled",
                "reason=escape");
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
            IsExplorerForeground);

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

        var target = _locator.FindAt(cursor.X, cursor.Y);
        if (target is null)
        {
            HideOverlay();
            return;
        }

        ShowOverlay(target);
    }

    private bool IsExplorerForeground()
    {
        var foreground = _native.GetForegroundWindow();
        return string.Equals(
            _native.GetProcessName(_native.GetProcessId(foreground)),
            "explorer",
            StringComparison.OrdinalIgnoreCase);
    }

    private void ShowOverlay(DiscordDropTarget target)
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
        var paths = GetImagePaths(e.Data);
        var target = _currentTarget;
        e.Effects = target is not null && paths.Length > 0
            ? WpfDragDropEffects.Copy
            : WpfDragDropEffects.None;
        e.Handled = true;
        if (target is null || paths.Length == 0)
        {
            return;
        }

        _passThrough.Observe(target, paths);
        _diagnostic?.Invoke(
            "discord-drop-observed",
            $"files={paths.Length}, window=0x{target.MainWindow.ToInt64():X}");
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
        DiscordDropTarget target,
        IReadOnlyList<string> paths)
    {
        _diagnostic?.Invoke(
            "discord-drop-released",
            $"files={paths.Count}, window=0x{target.MainWindow.ToInt64():X}");
        var result = await _captureRuntime.RegisterNativeDroppedFilesAsync(
            target,
            paths);
        _diagnostic?.Invoke(
            "discord-drop-result",
            $"result={result}, files={paths.Count}");
    }

    private static string[] GetImagePaths(WpfDataObject data)
    {
        if (!data.GetDataPresent(WpfDataFormats.FileDrop) ||
            data.GetData(WpfDataFormats.FileDrop) is not string[] paths)
        {
            return [];
        }

        return paths
            .Where(ClipboardImageCodec.IsSupportedImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
