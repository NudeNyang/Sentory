using System.Windows;
using System.Windows.Controls;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace Sentory.App;

public sealed class CenteredWrapPanel : WrapPanel
{
    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        if (Orientation != WpfOrientation.Horizontal)
        {
            return base.ArrangeOverride(finalSize);
        }

        var rowStart = 0;
        var rowWidth = 0d;
        var rowHeight = 0d;
        var rowTop = 0d;

        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var itemSize = GetItemSize(InternalChildren[index]);
            var startsNextRow = rowWidth > 0 &&
                                rowWidth + itemSize.Width > finalSize.Width;
            if (startsNextRow)
            {
                ArrangeRow(
                    rowStart,
                    index,
                    rowTop,
                    rowWidth,
                    rowHeight,
                    finalSize.Width);
                rowTop += rowHeight;
                rowStart = index;
                rowWidth = 0;
                rowHeight = 0;
            }

            rowWidth += itemSize.Width;
            rowHeight = Math.Max(rowHeight, itemSize.Height);
        }

        if (rowStart < InternalChildren.Count)
        {
            ArrangeRow(
                rowStart,
                InternalChildren.Count,
                rowTop,
                rowWidth,
                rowHeight,
                finalSize.Width);
        }

        return finalSize;
    }

    private void ArrangeRow(
        int startIndex,
        int endIndex,
        double top,
        double rowWidth,
        double rowHeight,
        double availableWidth)
    {
        var left = Math.Max((availableWidth - rowWidth) / 2, 0);
        for (var index = startIndex; index < endIndex; index++)
        {
            var child = InternalChildren[index];
            var itemSize = GetItemSize(child);
            child.Arrange(new WpfRect(
                left,
                top,
                itemSize.Width,
                Math.Max(rowHeight, itemSize.Height)));
            left += itemSize.Width;
        }
    }

    private WpfSize GetItemSize(UIElement child) =>
        new(
            double.IsNaN(ItemWidth)
                ? child.DesiredSize.Width
                : ItemWidth,
            double.IsNaN(ItemHeight)
                ? child.DesiredSize.Height
                : ItemHeight);
}
