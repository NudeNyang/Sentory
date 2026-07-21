using System.Windows;
using WpfRect = System.Windows.Rect;

namespace Sentory.App;

internal readonly record struct VirtualizingWrapLayout(
    int ItemCount,
    int ItemsPerRow,
    int RowCount,
    double AvailableWidth,
    double ItemWidth,
    double ItemHeight,
    int VisibleStartIndex,
    int VisibleEndIndex,
    int RealizedStartIndex,
    int RealizedEndIndex,
    double ExtentHeight)
{
    public static VirtualizingWrapLayout Calculate(
        int itemCount,
        double availableWidth,
        double itemWidth,
        double itemHeight,
        double verticalOffset,
        double viewportHeight,
        int cacheRows)
    {
        if (itemWidth <= 0 || double.IsNaN(itemWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(itemWidth));
        }

        if (itemHeight <= 0 || double.IsNaN(itemHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(itemHeight));
        }

        itemCount = Math.Max(0, itemCount);
        availableWidth = Math.Max(itemWidth, availableWidth);
        verticalOffset = Math.Max(0, verticalOffset);
        viewportHeight = Math.Max(itemHeight, viewportHeight);
        cacheRows = Math.Max(0, cacheRows);

        var itemsPerRow = Math.Max(
            1,
            (int)Math.Floor(availableWidth / itemWidth));
        var rowCount = itemCount == 0
            ? 0
            : (int)Math.Ceiling((double)itemCount / itemsPerRow);
        var visibleStartRow = Math.Clamp(
            (int)Math.Floor(verticalOffset / itemHeight),
            0,
            rowCount);
        var visibleEndRow = Math.Clamp(
            (int)Math.Ceiling(
                (verticalOffset + viewportHeight) / itemHeight),
            visibleStartRow,
            rowCount);
        var realizedStartRow = Math.Max(0, visibleStartRow - cacheRows);
        var realizedEndRow = Math.Min(
            rowCount,
            visibleEndRow + cacheRows);

        return new VirtualizingWrapLayout(
            itemCount,
            itemsPerRow,
            rowCount,
            availableWidth,
            itemWidth,
            itemHeight,
            Math.Min(itemCount, visibleStartRow * itemsPerRow),
            Math.Min(itemCount, visibleEndRow * itemsPerRow),
            Math.Min(itemCount, realizedStartRow * itemsPerRow),
            Math.Min(itemCount, realizedEndRow * itemsPerRow),
            rowCount * itemHeight);
    }

    public WpfRect GetItemRect(int itemIndex)
    {
        if (itemIndex < 0 || itemIndex >= ItemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(itemIndex));
        }

        var row = itemIndex / ItemsPerRow;
        var column = itemIndex % ItemsPerRow;
        var itemsInRow = Math.Min(
            ItemsPerRow,
            ItemCount - row * ItemsPerRow);
        var rowWidth = itemsInRow * ItemWidth;
        var left = Math.Max((AvailableWidth - rowWidth) / 2, 0);
        return new WpfRect(
            left + column * ItemWidth,
            row * ItemHeight,
            ItemWidth,
            ItemHeight);
    }
}
