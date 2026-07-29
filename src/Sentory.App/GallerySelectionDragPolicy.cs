using System.Windows;

namespace Sentory.App;

internal static class GallerySelectionDragPolicy
{
    internal const double AutoScrollEdgeSize = 72;
    private const double MinimumAutoScrollStep = 2;
    private const double MaximumAutoScrollStep = 8;

    public static double CalculateAutoScrollDelta(
        double pointerY,
        double viewportHeight,
        double verticalOffset,
        double scrollableHeight)
    {
        if (viewportHeight <= 0 || scrollableHeight <= 0)
        {
            return 0;
        }

        var edgeSize = Math.Min(
            AutoScrollEdgeSize,
            viewportHeight / 3);
        if (edgeSize <= 0)
        {
            return 0;
        }

        if (pointerY < edgeSize && verticalOffset > 0)
        {
            return -CalculateStep((edgeSize - pointerY) / edgeSize);
        }

        if (pointerY > viewportHeight - edgeSize &&
            verticalOffset < scrollableHeight)
        {
            return CalculateStep(
                (pointerY - (viewportHeight - edgeSize)) / edgeSize);
        }

        return 0;
    }

    public static IEnumerable<int> FindIntersectingItemIndices(
        VirtualizingWrapLayout layout,
        Rect selectionBounds)
    {
        for (var index = 0; index < layout.ItemCount; index++)
        {
            if (selectionBounds.IntersectsWith(layout.GetItemRect(index)))
            {
                yield return index;
            }
        }
    }

    private static double CalculateStep(double proximity)
    {
        proximity = Math.Clamp(proximity, 0, 1);
        return MinimumAutoScrollStep +
               (MaximumAutoScrollStep - MinimumAutoScrollStep) *
               proximity * proximity;
    }
}
