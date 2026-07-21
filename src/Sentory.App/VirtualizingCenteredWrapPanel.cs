using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WpfSize = System.Windows.Size;

namespace Sentory.App;

public sealed class VirtualizingCenteredWrapPanel : VirtualizingPanel
{
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(
            nameof(ItemWidth),
            typeof(double),
            typeof(VirtualizingCenteredWrapPanel),
            new FrameworkPropertyMetadata(
                268d,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VirtualizingCenteredWrapPanel),
            new FrameworkPropertyMetadata(
                336d,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty CacheRowsProperty =
        DependencyProperty.Register(
            nameof(CacheRows),
            typeof(int),
            typeof(VirtualizingCenteredWrapPanel),
            new FrameworkPropertyMetadata(
                2,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    private ScrollViewer? _scrollViewer;
    private VirtualizingWrapLayout _layout;

    internal VirtualizingWrapLayout CurrentLayout => _layout;

    public VirtualizingCenteredWrapPanel()
    {
        Loaded += Panel_Loaded;
        Unloaded += Panel_Unloaded;
    }

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public int CacheRows
    {
        get => (int)GetValue(CacheRowsProperty);
        set => SetValue(CacheRowsProperty, value);
    }

    internal IReadOnlyList<object> GetVisibleDataItems()
    {
        var items = new List<object>();
        var generator = ItemContainerGenerator;
        for (var childIndex = 0;
             childIndex < InternalChildren.Count;
             childIndex++)
        {
            var itemIndex = generator.IndexFromGeneratorPosition(
                new GeneratorPosition(childIndex, 0));
            if (itemIndex < _layout.VisibleStartIndex ||
                itemIndex >= _layout.VisibleEndIndex ||
                InternalChildren[childIndex] is not FrameworkElement
                {
                    DataContext: { } dataItem
                })
            {
                continue;
            }

            items.Add(dataItem);
        }

        return items;
    }

    internal IReadOnlyList<object> GetRealizedDataItems() =>
        InternalChildren
            .OfType<FrameworkElement>()
            .Select(child => child.DataContext)
            .Where(dataItem => dataItem is not null)
            .Cast<object>()
            .ToArray();

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        EnsureScrollViewer();
        var owner = ItemsControl.GetItemsOwner(this);
        var itemCount = owner?.Items.Count ?? 0;
        var width = ResolveWidth(availableSize.Width);
        var viewportHeight = ResolveViewportHeight(availableSize.Height);
        var verticalOffset = _scrollViewer?.VerticalOffset ?? 0;
        _layout = VirtualizingWrapLayout.Calculate(
            itemCount,
            width,
            ItemWidth,
            ItemHeight,
            verticalOffset,
            viewportHeight,
            CacheRows);

        RecycleContainersOutsideRange(
            _layout.RealizedStartIndex,
            _layout.RealizedEndIndex);
        RealizeAndMeasureRange(
            _layout.RealizedStartIndex,
            _layout.RealizedEndIndex);

        return new WpfSize(width, _layout.ExtentHeight);
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        var generator = ItemContainerGenerator;
        for (var childIndex = 0;
             childIndex < InternalChildren.Count;
             childIndex++)
        {
            var itemIndex = generator.IndexFromGeneratorPosition(
                new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0 || itemIndex >= _layout.ItemCount)
            {
                continue;
            }

            InternalChildren[childIndex].Arrange(
                _layout.GetItemRect(itemIndex));
        }

        return new WpfSize(finalSize.Width, _layout.ExtentHeight);
    }

    protected override void OnItemsChanged(
        object sender,
        ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        if (args.Action == NotifyCollectionChangedAction.Reset &&
            InternalChildren.Count > 0)
        {
            RemoveInternalChildRange(0, InternalChildren.Count);
        }

        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index)
    {
        if (_scrollViewer is null || index < 0 ||
            index >= _layout.ItemCount)
        {
            return;
        }

        var bounds = _layout.GetItemRect(index);
        if (bounds.Top < _scrollViewer.VerticalOffset)
        {
            _scrollViewer.ScrollToVerticalOffset(bounds.Top);
        }
        else if (bounds.Bottom >
                 _scrollViewer.VerticalOffset +
                 _scrollViewer.ViewportHeight)
        {
            _scrollViewer.ScrollToVerticalOffset(
                bounds.Bottom - _scrollViewer.ViewportHeight);
        }
    }

    private void RealizeAndMeasureRange(int startIndex, int endIndex)
    {
        if (startIndex >= endIndex)
        {
            return;
        }

        var generator = ItemContainerGenerator;
        var startPosition = generator.GeneratorPositionFromIndex(startIndex);
        var childIndex = startPosition.Offset == 0
            ? startPosition.Index
            : startPosition.Index + 1;
        using var generation = generator.StartAt(
            startPosition,
            GeneratorDirection.Forward,
            true);
        for (var itemIndex = startIndex;
             itemIndex < endIndex;
             itemIndex++, childIndex++)
        {
            if (generator.GenerateNext(out var newlyRealized)
                is not UIElement child)
            {
                continue;
            }

            var isInVisualTree = ReferenceEquals(
                VisualTreeHelper.GetParent(child),
                this);
            if (newlyRealized || !isInVisualTree)
            {
                if (childIndex >= InternalChildren.Count)
                {
                    AddInternalChild(child);
                }
                else
                {
                    InsertInternalChild(childIndex, child);
                }

                generator.PrepareItemContainer(child);
            }

            child.Measure(new WpfSize(ItemWidth, ItemHeight));
        }
    }

    private void RecycleContainersOutsideRange(
        int startIndex,
        int endIndex)
    {
        var generator = ItemContainerGenerator;
        for (var childIndex = InternalChildren.Count - 1;
             childIndex >= 0;
             childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex >= startIndex && itemIndex < endIndex)
            {
                continue;
            }

            if (generator is IRecyclingItemContainerGenerator recycling)
            {
                recycling.Recycle(position, 1);
            }
            else
            {
                generator.Remove(position, 1);
            }
            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private double ResolveWidth(double availableWidth)
    {
        if (!double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            return availableWidth;
        }

        if (_scrollViewer is { ViewportWidth: > 0 })
        {
            return _scrollViewer.ViewportWidth;
        }

        return Math.Max(ItemWidth, ActualWidth);
    }

    private double ResolveViewportHeight(double availableHeight)
    {
        if (_scrollViewer is { ViewportHeight: > 0 })
        {
            return _scrollViewer.ViewportHeight;
        }

        return !double.IsInfinity(availableHeight) && availableHeight > 0
            ? availableHeight
            : ItemHeight * 3;
    }

    private void Panel_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureScrollViewer();
        InvalidateMeasure();
    }

    private void Panel_Unloaded(object sender, RoutedEventArgs e) =>
        DetachScrollViewer();

    private void EnsureScrollViewer()
    {
        var scrollViewer = FindVisualAncestor<ScrollViewer>(this);
        if (ReferenceEquals(_scrollViewer, scrollViewer))
        {
            return;
        }

        DetachScrollViewer();
        _scrollViewer = scrollViewer;
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        }
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            _scrollViewer = null;
        }
    }

    private void ScrollViewer_ScrollChanged(
        object sender,
        ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) > double.Epsilon ||
            Math.Abs(e.ViewportHeightChange) > double.Epsilon ||
            Math.Abs(e.ViewportWidthChange) > double.Epsilon)
        {
            InvalidateMeasure();
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(child);
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T result)
            {
                return result;
            }
        }

        return null;
    }
}
