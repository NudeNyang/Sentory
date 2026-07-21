using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sentory.App.Tests;

public sealed class VirtualizingWrapLayoutTests
{
    [Fact]
    public void RealizesViewportPlusBufferedRowsInsteadOfEveryItem()
    {
        var layout = VirtualizingWrapLayout.Calculate(
            itemCount: 500,
            availableWidth: 1100,
            itemWidth: 268,
            itemHeight: 336,
            verticalOffset: 3360,
            viewportHeight: 700,
            cacheRows: 2);

        Assert.Equal(4, layout.ItemsPerRow);
        Assert.Equal(125 * 336, layout.ExtentHeight);
        Assert.True(layout.RealizedStartIndex > 0);
        Assert.True(layout.RealizedEndIndex < 500);
        Assert.InRange(
            layout.RealizedEndIndex - layout.RealizedStartIndex,
            20,
            32);
        Assert.True(layout.RealizedStartIndex <= layout.VisibleStartIndex);
        Assert.True(layout.RealizedEndIndex >= layout.VisibleEndIndex);
    }

    [Fact]
    public void CentersTheLastPartialRow()
    {
        var layout = VirtualizingWrapLayout.Calculate(
            itemCount: 6,
            availableWidth: 1100,
            itemWidth: 268,
            itemHeight: 336,
            verticalOffset: 0,
            viewportHeight: 700,
            cacheRows: 1);

        var firstItemInLastRow = layout.GetItemRect(4);

        Assert.Equal(282, firstItemInLastRow.X);
        Assert.Equal(336, firstItemInLastRow.Y);
    }

    [Fact]
    public void ItemsControlRealizesOnlyBufferedViewportAndRecyclesOnScroll()
    {
        RunOnSta(() =>
        {
            var panelFactory = new FrameworkElementFactory(
                typeof(VirtualizingCenteredWrapPanel));
            panelFactory.SetValue(
                VirtualizingCenteredWrapPanel.ItemWidthProperty,
                268d);
            panelFactory.SetValue(
                VirtualizingCenteredWrapPanel.ItemHeightProperty,
                336d);
            panelFactory.SetValue(
                VirtualizingCenteredWrapPanel.CacheRowsProperty,
                2);
            var itemsControl = new ItemsControl
            {
                ItemsSource = Enumerable.Range(0, 500).ToArray(),
                ItemsPanel = new ItemsPanelTemplate(panelFactory)
            };
            VirtualizingPanel.SetIsVirtualizing(itemsControl, true);
            VirtualizingPanel.SetVirtualizationMode(
                itemsControl,
                VirtualizationMode.Recycling);
            var scrollViewer = new ScrollViewer
            {
                Width = 1100,
                Height = 700,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Hidden,
                Content = itemsControl
            };

            scrollViewer.Measure(new Size(1100, 700));
            scrollViewer.Arrange(new Rect(0, 0, 1100, 700));
            scrollViewer.UpdateLayout();
            var panel = FindDescendant<VirtualizingCenteredWrapPanel>(
                itemsControl);

            Assert.NotNull(panel);
            Assert.InRange(panel.GetRealizedDataItems().Count, 12, 24);
            Assert.Contains(0, panel.GetRealizedDataItems().Cast<int>());

            scrollViewer.ScrollToVerticalOffset(3360);
            scrollViewer.UpdateLayout();
            panel.UpdateLayout();
            var scrolledItems = panel
                .GetRealizedDataItems()
                .Cast<int>()
                .ToArray();

            Assert.True(
                scrolledItems.Length is >= 20 and <= 32,
                $"Realized={scrolledItems.Length}; " +
                $"Items=[{string.Join(',', scrolledItems)}]; " +
                $"Layout={panel.CurrentLayout}; " +
                $"Offset={scrollViewer.VerticalOffset}; " +
                $"Viewport={scrollViewer.ViewportHeight}; " +
                $"Extent={scrollViewer.ExtentHeight}");
            Assert.DoesNotContain(0, scrolledItems);
            Assert.Contains(40, scrolledItems);
        });
    }

    private static T? FindDescendant<T>(DependencyObject current)
        where T : DependencyObject
    {
        if (current is T match)
        {
            return match;
        }

        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(current);
             index++)
        {
            var descendant = FindDescendant<T>(
                VisualTreeHelper.GetChild(current, index));
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
