using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Sentory.Core;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Sentory.App;

internal sealed class OverlayScrollIndicatorController : IDisposable
{
    private const double RevealDistance = 44;
    private readonly ScrollViewer _scrollViewer;
    private readonly FrameworkElement _surface;
    private readonly Border _track;
    private readonly Border _thumb;
    private readonly TranslateTransform _thumbTransform;
    private readonly DispatcherTimer _hideTimer;
    private bool _near;
    private bool _active;
    private bool _dragging;
    private bool _shown;
    private bool _emphasized;

    public OverlayScrollIndicatorController(
        ScrollViewer scrollViewer,
        FrameworkElement surface,
        Border track,
        Border thumb,
        TranslateTransform thumbTransform)
    {
        _scrollViewer = scrollViewer;
        _surface = surface;
        _track = track;
        _thumb = thumb;
        _thumbTransform = thumbTransform;
        _hideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1200)
        };
        _hideTimer.Tick += HideTimer_Tick;
        _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        _surface.PreviewMouseMove += Surface_PreviewMouseMove;
        _surface.MouseLeave += Surface_MouseLeave;
        _track.MouseEnter += Track_MouseEnter;
        _track.MouseLeave += Track_MouseLeave;
        _track.PreviewMouseLeftButtonDown += Track_MouseLeftButtonDown;
        _track.PreviewMouseMove += Track_PreviewMouseMove;
        _track.PreviewMouseLeftButtonUp += Track_MouseLeftButtonUp;
        _track.PreviewMouseWheel += Track_PreviewMouseWheel;
        _track.LostMouseCapture += Track_LostMouseCapture;
        _track.SizeChanged += Track_SizeChanged;
        _surface.Loaded += Surface_Loaded;
    }

    private void Surface_Loaded(object sender, RoutedEventArgs e) => Update();

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        Update();
        if (Math.Abs(e.VerticalChange) > double.Epsilon)
        {
            _active = true;
            _hideTimer.Stop();
            _hideTimer.Start();
            UpdateVisibility();
        }
    }

    private void Surface_PreviewMouseMove(object sender, MouseEventArgs e) =>
        UpdateProximity(e.GetPosition(_surface));

    private void Surface_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            SetNear(false);
        }
    }

    private void Track_MouseEnter(object sender, MouseEventArgs e)
    {
        SetNear(true);
        SetEmphasis(true);
    }

    private void Track_MouseLeave(object sender, MouseEventArgs e)
    {
        SetEmphasis(_dragging);
        if (!_dragging)
        {
            UpdateProximity(Mouse.GetPosition(_surface));
        }
    }

    private void Track_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_track.IsHitTestVisible)
        {
            return;
        }

        _dragging = true;
        _active = true;
        _hideTimer.Stop();
        SetNear(true);
        SetEmphasis(true);
        _track.CaptureMouse();
        ScrollToPointer(e.GetPosition(_track).Y);
        e.Handled = true;
    }

    private void Track_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        ScrollToPointer(e.GetPosition(_track).Y);
        e.Handled = true;
    }

    private void Track_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging && e.ChangedButton == MouseButton.Left)
        {
            FinishDrag();
            e.Handled = true;
        }
    }

    private void Track_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            FinishDrag();
        }
    }

    private void Track_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_track.IsHitTestVisible || e.Delta == 0)
        {
            return;
        }

        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0)
        {
            return;
        }

        var notches = Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine);
        if (lines < 0)
        {
            for (var i = 0; i < notches; i++)
            {
                if (e.Delta > 0) _scrollViewer.PageUp(); else _scrollViewer.PageDown();
            }
        }
        else
        {
            for (var i = 0; i < notches * lines; i++)
            {
                if (e.Delta > 0) _scrollViewer.LineUp(); else _scrollViewer.LineDown();
            }
        }

        e.Handled = true;
    }

    private void Track_SizeChanged(object sender, SizeChangedEventArgs e) => Update();

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        _active = false;
        UpdateVisibility();
    }

    private void Update()
    {
        var metrics = Metrics();
        _track.Visibility = metrics.IsScrollable ? Visibility.Visible : Visibility.Collapsed;
        _track.IsHitTestVisible = metrics.IsScrollable;
        if (!metrics.IsScrollable)
        {
            _hideTimer.Stop();
            _near = false;
            _active = false;
            SetEmphasis(false);
            SetShown(false);
            return;
        }

        _thumb.Height = metrics.ThumbHeight;
        _thumbTransform.Y = metrics.ThumbTop;
        UpdateVisibility();
    }

    private void UpdateProximity(Point position)
    {
        if (_dragging)
        {
            SetNear(true);
            return;
        }

        if (!_track.IsHitTestVisible || _track.ActualHeight <= 0)
        {
            SetNear(false);
            return;
        }

        var topLeft = _track.TranslatePoint(new Point(), _surface);
        var bounds = new Rect(topLeft, _track.RenderSize);
        var dx = Math.Max(Math.Max(bounds.Left - position.X, 0), position.X - bounds.Right);
        var dy = Math.Max(Math.Max(bounds.Top - position.Y, 0), position.Y - bounds.Bottom);
        SetNear(Math.Sqrt(dx * dx + dy * dy) <= RevealDistance);
    }

    private void SetNear(bool value)
    {
        if (_near == value) return;
        _near = value;
        UpdateVisibility();
    }

    private void UpdateVisibility() =>
        SetShown(_track.IsHitTestVisible && (_near || _active || _dragging));

    private void SetShown(bool value)
    {
        if (_shown == value) return;
        _shown = value;
        _track.BeginAnimation(UIElement.OpacityProperty, Animate(value ? 1 : 0, 160));
    }

    private void SetEmphasis(bool value)
    {
        if (_emphasized == value) return;
        _emphasized = value;
        _thumb.BeginAnimation(FrameworkElement.WidthProperty, Animate(value ? 6 : 3, 140));
        _thumb.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation
        {
            To = value ? new Thickness(0, 0, 2, 0) : new Thickness(0, 0, 3, 0),
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        });
        _thumb.BeginAnimation(UIElement.OpacityProperty, Animate(value ? 0.95 : 0.46, 140));
    }

    private void ScrollToPointer(double y)
    {
        var metrics = Metrics();
        if (!metrics.IsScrollable || metrics.ThumbTravel <= 0) return;
        var top = Math.Clamp(y - metrics.ThumbHeight / 2, 0, metrics.ThumbTravel);
        _scrollViewer.ScrollToVerticalOffset(top / metrics.ThumbTravel * _scrollViewer.ScrollableHeight);
    }

    private ScrollIndicatorMetrics Metrics() => ScrollIndicatorMetrics.Calculate(
        _track.ActualHeight,
        _scrollViewer.ExtentHeight,
        _scrollViewer.ViewportHeight,
        _scrollViewer.VerticalOffset);

    private void FinishDrag()
    {
        _dragging = false;
        if (_track.IsMouseCaptured) _track.ReleaseMouseCapture();
        SetEmphasis(_track.IsMouseOver);
        UpdateProximity(Mouse.GetPosition(_surface));
        UpdateVisibility();
    }

    private static DoubleAnimation Animate(double value, int milliseconds) => new()
    {
        To = value,
        Duration = TimeSpan.FromMilliseconds(milliseconds),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        FillBehavior = FillBehavior.HoldEnd
    };

    public void Dispose()
    {
        _hideTimer.Stop();
        _hideTimer.Tick -= HideTimer_Tick;
        _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
        _surface.PreviewMouseMove -= Surface_PreviewMouseMove;
        _surface.MouseLeave -= Surface_MouseLeave;
        _track.MouseEnter -= Track_MouseEnter;
        _track.MouseLeave -= Track_MouseLeave;
        _track.PreviewMouseLeftButtonDown -= Track_MouseLeftButtonDown;
        _track.PreviewMouseMove -= Track_PreviewMouseMove;
        _track.PreviewMouseLeftButtonUp -= Track_MouseLeftButtonUp;
        _track.PreviewMouseWheel -= Track_PreviewMouseWheel;
        _track.LostMouseCapture -= Track_LostMouseCapture;
        _track.SizeChanged -= Track_SizeChanged;
        _surface.Loaded -= Surface_Loaded;
    }
}
