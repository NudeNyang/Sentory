using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media;
using WpfClipboard = System.Windows.Clipboard;
using WpfControl = System.Windows.Controls.Control;
using WpfDependencyObject = System.Windows.DependencyObject;
using WpfDependencyProperty = System.Windows.DependencyProperty;
using WpfDrawingContext = System.Windows.Media.DrawingContext;
using WpfFrameworkPropertyMetadata = System.Windows.FrameworkPropertyMetadata;
using WpfFrameworkPropertyMetadataOptions =
    System.Windows.FrameworkPropertyMetadataOptions;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;
using WpfTextAlignment = System.Windows.TextAlignment;
using WpfTextTrimming = System.Windows.TextTrimming;
using WpfTextWrapping = System.Windows.TextWrapping;
using WpfWindow = System.Windows.Window;

namespace Sentory.App;

public sealed class SelectableTextBlock : WpfControl
{
    private const double ExtendedHorizontalHitArea = 22;
    private const double ExtendedVerticalHitArea = 8;

    private static readonly System.Windows.Media.Brush SelectionBrush =
        new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(151, 205, 239));

    private static WeakReference<SelectableTextBlock>? _activeSelection;

    public static readonly WpfDependencyProperty TextProperty =
        WpfDependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SelectableTextBlock),
            CreateTextMetadata(string.Empty, OnTextChanged));

    public static readonly WpfDependencyProperty TextWrappingProperty =
        WpfDependencyProperty.Register(
            nameof(TextWrapping),
            typeof(WpfTextWrapping),
            typeof(SelectableTextBlock),
            CreateTextMetadata(WpfTextWrapping.NoWrap));

    public static readonly WpfDependencyProperty TextTrimmingProperty =
        WpfDependencyProperty.Register(
            nameof(TextTrimming),
            typeof(WpfTextTrimming),
            typeof(SelectableTextBlock),
            CreateTextMetadata(WpfTextTrimming.None));

    public static readonly WpfDependencyProperty TextAlignmentProperty =
        WpfDependencyProperty.Register(
            nameof(TextAlignment),
            typeof(WpfTextAlignment),
            typeof(SelectableTextBlock),
            CreateTextMetadata(WpfTextAlignment.Left));

    public static readonly WpfDependencyProperty LineHeightProperty =
        WpfDependencyProperty.Register(
            nameof(LineHeight),
            typeof(double),
            typeof(SelectableTextBlock),
            CreateTextMetadata(double.NaN));

    private int _selectionAnchor;
    private int _selectionStart;
    private int _selectionLength;
    private bool _isSelecting;

    public SelectableTextBlock()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        Cursor = System.Windows.Input.Cursors.IBeam;
        Focusable = true;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public WpfTextWrapping TextWrapping
    {
        get => (WpfTextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public WpfTextTrimming TextTrimming
    {
        get => (WpfTextTrimming)GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    public WpfTextAlignment TextAlignment
    {
        get => (WpfTextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    public int SelectionStart => _selectionStart;

    public int SelectionLength => _selectionLength;

    public string SelectedText => _selectionLength == 0
        ? string.Empty
        : Text.Substring(_selectionStart, _selectionLength);

    public static void EnableExtendedHitTesting(WpfWindow window)
    {
        MouseButtonEventHandler previewMouseDown = (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left ||
                e.OriginalSource is not WpfDependencyObject source ||
                FindAncestor<SelectableTextBlock>(source) is not null ||
                IsInteractiveSource(source))
            {
                return;
            }

            var target = FindExtendedHitTarget(window, e);
            if (target is null)
            {
                return;
            }

            target.BeginSelection(e.GetPosition(target), e.ClickCount);
            e.Handled = true;
        };

        window.AddHandler(
            Mouse.PreviewMouseDownEvent,
            previewMouseDown,
            handledEventsToo: true);
        window.Closed += RemoveHandler;
        return;

        void RemoveHandler(object? sender, EventArgs e)
        {
            window.RemoveHandler(
                Mouse.PreviewMouseDownEvent,
                previewMouseDown);
            window.Closed -= RemoveHandler;
        }
    }

    protected override WpfSize MeasureOverride(WpfSize constraint)
    {
        var availableWidth = double.IsInfinity(constraint.Width)
            ? 100000
            : Math.Max(1, constraint.Width - Padding.Left - Padding.Right);
        var formatted = CreateFormattedText(availableWidth);
        var desiredWidth = TextWrapping == WpfTextWrapping.NoWrap
            ? formatted.WidthIncludingTrailingWhitespace
            : Math.Min(availableWidth, formatted.WidthIncludingTrailingWhitespace);
        return new WpfSize(
            desiredWidth + Padding.Left + Padding.Right,
            formatted.Height + Padding.Top + Padding.Bottom);
    }

    protected override void OnRender(WpfDrawingContext drawingContext)
    {
        if (Background is not null)
        {
            drawingContext.DrawRectangle(
                Background,
                null,
                new WpfRect(RenderSize));
        }

        var contentWidth = Math.Max(
            1,
            RenderSize.Width - Padding.Left - Padding.Right);
        var formatted = CreateFormattedText(contentWidth);
        DrawSelection(drawingContext, formatted);
        drawingContext.DrawText(
            formatted,
            new WpfPoint(Padding.Left, Padding.Top));
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            base.OnPreviewMouseDown(e);
            return;
        }

        var pointer = e.GetPosition(this);
        if (!IsWithinExtendedTextHitArea(pointer))
        {
            base.OnPreviewMouseDown(e);
            return;
        }

        BeginSelection(pointer, e.ClickCount);
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(WpfMouseEventArgs e)
    {
        if (!_isSelecting || e.LeftButton != MouseButtonState.Pressed)
        {
            base.OnPreviewMouseMove(e);
            return;
        }

        UpdateSelection(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        if (!_isSelecting || e.ChangedButton != MouseButton.Left)
        {
            base.OnPreviewMouseUp(e);
            return;
        }

        UpdateSelection(e.GetPosition(this));
        _isSelecting = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(WpfMouseEventArgs e)
    {
        _isSelecting = false;
        base.OnLostMouseCapture(e);
    }

    protected override void OnPreviewKeyDown(WpfKeyEventArgs e)
    {
        if (e.Key == Key.C &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            _selectionLength > 0)
        {
            try
            {
                WpfClipboard.SetText(SelectedText);
            }
            catch (COMException)
            {
                // Another process owns the clipboard momentarily.
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.A &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            TakeSelectionOwnership();
            SetSelection(0, Text.Length);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void DrawSelection(
        WpfDrawingContext drawingContext,
        FormattedText formatted)
    {
        if (_selectionLength <= 0 || string.IsNullOrEmpty(Text))
        {
            return;
        }

        var characterBoxes = GetCharacterBoxes(formatted)
            .Where(box =>
                box.Index >= _selectionStart &&
                box.Index < _selectionStart + _selectionLength)
            .ToArray();
        if (characterBoxes.Length == 0)
        {
            return;
        }

        var lineHeight = ResolveLineHeight();
        var selectionHeight = ResolveSelectionHeight(lineHeight);
        foreach (var line in characterBoxes.GroupBy(box =>
                     GetLineIndex(box.Bounds, lineHeight)))
        {
            var left = line.Min(box => box.Bounds.Left);
            var right = line.Max(box => box.Bounds.Right);
            var top = Padding.Top +
                line.Key * lineHeight +
                (lineHeight - selectionHeight) / 2;
            drawingContext.DrawRectangle(
                SelectionBrush,
                null,
                new WpfRect(
                    left,
                    top,
                    Math.Max(1, right - left),
                    selectionHeight));
        }
    }

    private void UpdateSelection(WpfPoint pointer)
    {
        var insertionIndex = GetTextInsertionIndex(pointer);
        var selectionStart = Math.Min(_selectionAnchor, insertionIndex);
        SetSelection(
            selectionStart,
            Math.Abs(insertionIndex - _selectionAnchor));
    }

    private void BeginSelection(WpfPoint pointer, int clickCount)
    {
        TakeSelectionOwnership();
        Focus();
        var insertionIndex = GetTextInsertionIndex(pointer);
        if (clickCount > 1)
        {
            SelectWordAt(insertionIndex);
            return;
        }

        _selectionAnchor = insertionIndex;
        _isSelecting = true;
        SetSelection(_selectionAnchor, 0);
        Mouse.Capture(this, CaptureMode.Element);
    }

    private void TakeSelectionOwnership()
    {
        if (_activeSelection?.TryGetTarget(out var previous) == true &&
            !ReferenceEquals(previous, this))
        {
            previous.ClearSelection();
        }

        _activeSelection = new WeakReference<SelectableTextBlock>(this);
    }

    private void ClearSelection()
    {
        _isSelecting = false;
        SetSelection(0, 0);
    }

    private int GetTextInsertionIndex(WpfPoint pointer)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return 0;
        }

        var contentWidth = Math.Max(
            1,
            ActualWidth - Padding.Left - Padding.Right);
        var boxes = GetCharacterBoxes(CreateFormattedText(contentWidth))
            .ToArray();
        if (boxes.Length == 0)
        {
            return pointer.X <= ActualWidth / 2 ? 0 : Text.Length;
        }

        var lineHeight = ResolveLineHeight();
        var requestedLine = Math.Max(
            0,
            (int)Math.Floor((pointer.Y - Padding.Top) / lineHeight));
        var availableLines = boxes
            .GroupBy(box => GetLineIndex(box.Bounds, lineHeight))
            .ToArray();
        var line = availableLines.MinBy(group =>
            Math.Abs(group.Key - requestedLine))!;
        var ordered = line.OrderBy(box => box.Bounds.Left).ToArray();

        if (pointer.X <= ordered[0].Bounds.Left)
        {
            return ordered[0].Index;
        }

        foreach (var box in ordered)
        {
            if (pointer.X <= box.Bounds.Left + box.Bounds.Width / 2)
            {
                return box.Index;
            }

            if (pointer.X <= box.Bounds.Right)
            {
                return box.Index + 1;
            }
        }

        return Math.Min(Text.Length, ordered[^1].Index + 1);
    }

    private FormattedText CreateFormattedText(double availableWidth)
    {
        var formatted = new FormattedText(
            Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = TextAlignment,
            Trimming = TextTrimming,
            MaxTextWidth = TextWrapping == WpfTextWrapping.NoWrap
                ? 100000
                : Math.Max(1, availableWidth)
        };

        if (!double.IsNaN(LineHeight) && LineHeight > 0)
        {
            formatted.LineHeight = LineHeight;
        }

        return formatted;
    }

    private IEnumerable<CharacterBox> GetCharacterBoxes(
        FormattedText formatted)
    {
        var origin = new WpfPoint(Padding.Left, Padding.Top);
        for (var index = 0; index < Text.Length; index++)
        {
            var geometry = formatted.BuildHighlightGeometry(origin, index, 1);
            if (geometry is null || geometry.Bounds.IsEmpty)
            {
                continue;
            }

            yield return new CharacterBox(index, geometry.Bounds);
        }
    }

    private double ResolveLineHeight()
    {
        if (!double.IsNaN(LineHeight) && LineHeight > 0)
        {
            return LineHeight;
        }

        return Math.Max(FontSize, FontFamily.LineSpacing * FontSize);
    }

    private double ResolveSelectionHeight(double lineHeight) =>
        Math.Min(
            lineHeight,
            Math.Max(FontSize, FontFamily.LineSpacing * FontSize));

    private int GetLineIndex(WpfRect bounds, double lineHeight) =>
        Math.Max(
            0,
            (int)Math.Round(
                (bounds.Top - Padding.Top) / lineHeight,
                MidpointRounding.AwayFromZero));

    private void SetSelection(int start, int length)
    {
        var boundedStart = Math.Clamp(start, 0, Text.Length);
        var boundedLength = Math.Clamp(length, 0, Text.Length - boundedStart);
        if (_selectionStart == boundedStart &&
            _selectionLength == boundedLength)
        {
            return;
        }

        _selectionStart = boundedStart;
        _selectionLength = boundedLength;
        InvalidateVisual();
    }

    private void SelectWordAt(int insertionIndex)
    {
        if (string.IsNullOrEmpty(Text))
        {
            SetSelection(0, 0);
            return;
        }

        var characterIndex = Math.Clamp(insertionIndex, 0, Text.Length - 1);
        if (char.IsWhiteSpace(Text[characterIndex]) && characterIndex > 0)
        {
            characterIndex--;
        }

        var start = characterIndex;
        while (start > 0 && !char.IsWhiteSpace(Text[start - 1]))
        {
            start--;
        }

        var end = characterIndex + 1;
        while (end < Text.Length && !char.IsWhiteSpace(Text[end]))
        {
            end++;
        }

        SetSelection(start, end - start);
    }

    private static WpfFrameworkPropertyMetadata CreateTextMetadata(
        object defaultValue,
        System.Windows.PropertyChangedCallback? changed = null) =>
        new(
            defaultValue,
            WpfFrameworkPropertyMetadataOptions.AffectsMeasure |
            WpfFrameworkPropertyMetadataOptions.AffectsRender,
            changed);

    private static void OnTextChanged(
        WpfDependencyObject dependencyObject,
        System.Windows.DependencyPropertyChangedEventArgs e)
    {
        var control = (SelectableTextBlock)dependencyObject;
        control.SetSelection(0, 0);
    }

    private static SelectableTextBlock? FindExtendedHitTarget(
        WpfWindow window,
        MouseButtonEventArgs e)
    {
        SelectableTextBlock? closest = null;
        var closestDistance = double.PositiveInfinity;
        foreach (var candidate in FindSelectableDescendants(window))
        {
            if (!candidate.IsVisible ||
                !candidate.IsEnabled ||
                candidate.ActualWidth <= 0 ||
                candidate.ActualHeight <= 0 ||
                string.IsNullOrEmpty(candidate.Text))
            {
                continue;
            }

            var point = e.GetPosition(candidate);
            var textBounds = candidate.GetRenderedTextBounds();
            if (point.X < textBounds.Left - ExtendedHorizontalHitArea ||
                point.X > textBounds.Right + ExtendedHorizontalHitArea ||
                point.Y < textBounds.Top - ExtendedVerticalHitArea ||
                point.Y > textBounds.Bottom + ExtendedVerticalHitArea)
            {
                continue;
            }

            var horizontalDistance = point.X < textBounds.Left
                ? textBounds.Left - point.X
                : point.X > textBounds.Right
                    ? point.X - textBounds.Right
                    : 0;
            var verticalDistance = point.Y < textBounds.Top
                ? textBounds.Top - point.Y
                : point.Y > textBounds.Bottom
                    ? point.Y - textBounds.Bottom
                    : 0;
            var distance =
                horizontalDistance * horizontalDistance +
                verticalDistance * verticalDistance;
            if (distance >= closestDistance)
            {
                continue;
            }

            closest = candidate;
            closestDistance = distance;
        }

        return closest;
    }

    private bool IsWithinExtendedTextHitArea(WpfPoint point)
    {
        var textBounds = GetRenderedTextBounds();
        return point.X >= textBounds.Left - ExtendedHorizontalHitArea &&
            point.X <= textBounds.Right + ExtendedHorizontalHitArea &&
            point.Y >= textBounds.Top - ExtendedVerticalHitArea &&
            point.Y <= textBounds.Bottom + ExtendedVerticalHitArea;
    }

    private WpfRect GetRenderedTextBounds()
    {
        var contentWidth = Math.Max(
            1,
            ActualWidth - Padding.Left - Padding.Right);
        var boxes = GetCharacterBoxes(CreateFormattedText(contentWidth))
            .Select(box => box.Bounds)
            .ToArray();
        if (boxes.Length == 0)
        {
            return new WpfRect(
                Padding.Left,
                Padding.Top,
                0,
                ResolveLineHeight());
        }

        var left = boxes.Min(bounds => bounds.Left);
        var top = boxes.Min(bounds => bounds.Top);
        var right = boxes.Max(bounds => bounds.Right);
        var bottom = boxes.Max(bounds => bounds.Bottom);
        return new WpfRect(
            left,
            top,
            Math.Max(0, right - left),
            Math.Max(0, bottom - top));
    }

    private static IEnumerable<SelectableTextBlock> FindSelectableDescendants(
        WpfDependencyObject parent)
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(parent);
             index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is SelectableTextBlock selectable)
            {
                yield return selectable;
            }

            foreach (var descendant in FindSelectableDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsInteractiveSource(WpfDependencyObject source) =>
        FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source)
            is not null ||
        FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source)
            is not null;

    private static T? FindAncestor<T>(WpfDependencyObject? source)
        where T : WpfDependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = source switch
            {
                System.Windows.Media.Visual or
                System.Windows.Media.Media3D.Visual3D =>
                    VisualTreeHelper.GetParent(source),
                System.Windows.ContentElement content =>
                    System.Windows.ContentOperations.GetParent(content),
                _ => null
            };
        }

        return null;
    }

    private sealed record CharacterBox(int Index, WpfRect Bounds);
}
