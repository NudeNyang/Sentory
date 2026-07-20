using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using Sentory.Platform.Windows.Interop;
using WpfDataObject = System.Windows.IDataObject;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfDataFormats = System.Windows.DataFormats;

namespace Sentory.Platform.Windows.Runtime;

public sealed class KakaoDropOverlayRuntime : IDisposable
{
    private const int MinimumDragDistance = 8;

    private readonly INativeWindowApi _native;
    private readonly IKakaoDropWindowApi _dropWindows;
    private readonly KakaoDropTargetLocator _locator;
    private readonly KakaoCaptureRuntime _captureRuntime;
    private readonly Func<bool> _isDarkTheme;
    private readonly Func<string> _headingText;
    private readonly Func<string> _descriptionText;
    private readonly Action<string, string>? _diagnostic;
    private readonly DispatcherTimer _timer;
    private readonly Window _window;
    private readonly Border _surface;
    private readonly TextBlock _heading;
    private readonly TextBlock _description;
    private readonly KakaoDropPassThroughState _passThrough = new();
    private bool _started;
    private bool _leftWasDown;
    private bool _explorerDragCandidate;
    private bool _oleDragOver;
    private (int X, int Y) _dragStart;
    private KakaoDropTarget? _currentTarget;

    public KakaoDropOverlayRuntime(
        KakaoCaptureRuntime captureRuntime,
        Func<bool> isDarkTheme,
        Func<string> headingText,
        Func<string> descriptionText,
        Action<string, string>? diagnostic = null)
    {
        _native = new NativeWindowApi();
        _dropWindows = (IKakaoDropWindowApi)_native;
        _locator = new KakaoDropTargetLocator(_native, _dropWindows);
        _captureRuntime = captureRuntime;
        _isDarkTheme = isDarkTheme;
        _headingText = headingText;
        _descriptionText = descriptionText;
        _diagnostic = diagnostic;

        _heading = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        _description = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = Visibility.Collapsed
        };
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            Margin = new Thickness(14, 8, 14, 8)
        };
        content.Children.Add(_heading);
        content.Children.Add(_description);

        _surface = new Border
        {
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Child = content
        };
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
            Content = _surface,
            SizeToContent = SizeToContent.Manual
        };
        _window.DragEnter += OnDragEnter;
        _window.DragOver += OnDragOver;
        _window.DragLeave += OnDragLeave;
        _window.Drop += OnDrop;

        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
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
        var leftDown = _dropWindows.IsLeftMouseButtonDown();
        var cursor = _dropWindows.GetCursorPosition();
        if (_passThrough.IsActive && _dropWindows.IsEscapeKeyDown())
        {
            _passThrough.Cancel();
            _leftWasDown = false;
            _explorerDragCandidate = false;
            _oleDragOver = false;
            _diagnostic?.Invoke(
                "kakao-drop-cancelled",
                "reason=escape");
            return;
        }

        if (_passThrough.TryTakeCompleted(
                leftDown,
                out var completedTarget,
                out var completedPaths))
        {
            _leftWasDown = false;
            _explorerDragCandidate = false;
            _oleDragOver = false;
            if (!_locator.IsWithinTargetBounds(
                    completedTarget,
                    cursor.X,
                    cursor.Y))
            {
                _diagnostic?.Invoke(
                    "kakao-drop-cancelled",
                    "reason=released-outside-target-bounds");
                return;
            }

            _ = CapturePassedThroughDropAsync(
                completedTarget,
                completedPaths);
            return;
        }

        if (_passThrough.IsActive)
        {
            return;
        }

        if (!leftDown)
        {
            if (_oleDragOver)
            {
                return;
            }

            _leftWasDown = false;
            _explorerDragCandidate = false;
            HideOverlay();
            return;
        }

        if (!_leftWasDown)
        {
            _leftWasDown = true;
            _dragStart = cursor;
            var foreground = _native.GetForegroundWindow();
            _explorerDragCandidate = string.Equals(
                _native.GetProcessName(_native.GetProcessId(foreground)),
                "explorer",
                StringComparison.OrdinalIgnoreCase);
        }

        if (!_explorerDragCandidate ||
            DistanceFromStart(cursor) < MinimumDragDistance)
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

    private double DistanceFromStart((int X, int Y) cursor)
    {
        var x = cursor.X - _dragStart.X;
        var y = cursor.Y - _dragStart.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    private void ShowOverlay(KakaoDropTarget target)
    {
        _currentTarget = target;
        ApplyTheme();
        _heading.Text = _headingText();
        _description.Text = _descriptionText();

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        var handle = new WindowInteropHelper(_window).Handle;
        _dropWindows.PositionTopmostWindow(handle, target.ChatBounds);
    }

    private void ApplyTheme()
    {
        _ = _isDarkTheme();
        _surface.Background = new SolidColorBrush(
            WpfColor.FromArgb(1, 255, 255, 255));
        _surface.BorderBrush = WpfBrushes.Transparent;
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

    private void OnDragLeave(object sender, WpfDragEventArgs e)
    {
        _oleDragOver = false;
    }

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
            "kakao-drop-observed",
            $"files={paths.Length}, chat=0x{target.ChatRootWindow.ToInt64():X}");
        _oleDragOver = false;
        _currentTarget = null;
        _window.Hide();
    }

    private void OnDrop(object sender, WpfDragEventArgs e)
    {
        // A valid drag is passed through before mouse release, so KakaoTalk
        // should own the real Drop event. Reaching this fallback means the
        // underlying messenger never received the native drag.
        e.Effects = WpfDragDropEffects.None;
        e.Handled = false;
        _oleDragOver = false;
        _explorerDragCandidate = false;
        HideOverlay();
    }

    private async Task CapturePassedThroughDropAsync(
        KakaoDropTarget target,
        IReadOnlyList<string> paths)
    {
        _diagnostic?.Invoke(
            "kakao-drop-released",
            $"files={paths.Count}, chat=0x{target.ChatRootWindow.ToInt64():X}");
        var result = await _captureRuntime.CaptureNativeDroppedFilesAsync(
            target,
            paths);
        _diagnostic?.Invoke(
            "kakao-drop-result",
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
