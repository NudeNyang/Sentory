using SkiaSharp;

namespace Sentory.Platform.Windows.Ocr;

internal static class OcrTextBlockFilter
{
    private const double MinimumHeightRatio = 0.01;
    private const double MinimumHeightPixels = 10;

    public static bool IsReadable(
        IReadOnlyList<SKPointI>? points,
        int imageWidth,
        int imageHeight)
    {
        if (points is null || points.Count < 4 ||
            imageWidth <= 0 || imageHeight <= 0)
        {
            return true;
        }

        var leftHeight = Distance(points[0], points[3]);
        var rightHeight = Distance(points[1], points[2]);
        var lineHeight = (leftHeight + rightHeight) / 2;
        var threshold = Math.Max(
            MinimumHeightPixels,
            Math.Min(imageWidth, imageHeight) * MinimumHeightRatio);
        return lineHeight >= threshold;
    }

    private static double Distance(SKPointI first, SKPointI second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}
