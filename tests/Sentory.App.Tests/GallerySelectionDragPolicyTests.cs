using System.Windows;

namespace Sentory.App.Tests;

public sealed class GallerySelectionDragPolicyTests
{
    [Theory]
    [InlineData(300, 700, 1000, 5000, 0)]
    [InlineData(20, 700, 1000, 5000, -4)]
    [InlineData(680, 700, 1000, 5000, 4)]
    public void AutoScrollsOnlyNearTheViewportEdges(
        double pointerY,
        double viewportHeight,
        double verticalOffset,
        double scrollableHeight,
        int expectedDirection)
    {
        var delta = GallerySelectionDragPolicy.CalculateAutoScrollDelta(
            pointerY,
            viewportHeight,
            verticalOffset,
            scrollableHeight);

        Assert.Equal(Math.Sign(expectedDirection), Math.Sign(delta));
        if (expectedDirection != 0)
        {
            Assert.InRange(Math.Abs(delta), 2, 8);
        }
    }

    [Theory]
    [InlineData(20, 0)]
    [InlineData(680, 5000)]
    public void StopsAtTheAvailableScrollBoundary(
        double pointerY,
        double verticalOffset)
    {
        var delta = GallerySelectionDragPolicy.CalculateAutoScrollDelta(
            pointerY,
            viewportHeight: 700,
            verticalOffset,
            scrollableHeight: 5000);

        Assert.Equal(0, delta);
    }

    [Fact]
    public void SelectsItemsAcrossUnrealizedRowsInContentCoordinates()
    {
        var layout = VirtualizingWrapLayout.Calculate(
            itemCount: 100,
            availableWidth: 1100,
            itemWidth: 268,
            itemHeight: 336,
            verticalOffset: 3360,
            viewportHeight: 700,
            cacheRows: 2);
        var selection = new Rect(
            x: 0,
            y: 100,
            width: 1100,
            height: 336 * 20);

        var selected = GallerySelectionDragPolicy
            .FindIntersectingItemIndices(layout, selection)
            .ToArray();

        Assert.Equal(84, selected.Length);
        Assert.Equal(0, selected[0]);
        Assert.Equal(83, selected[^1]);
        Assert.True(selected[0] < layout.RealizedStartIndex);
        Assert.True(selected[^1] > layout.RealizedEndIndex);
    }
}
