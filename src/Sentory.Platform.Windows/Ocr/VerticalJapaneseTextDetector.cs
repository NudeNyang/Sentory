using SkiaSharp;

namespace Sentory.Platform.Windows.Ocr;

internal sealed record DetectedTextGeometry(
    string Text,
    IReadOnlyList<SKPointI> Points);

internal static class VerticalJapaneseTextDetector
{
    private const double MinimumVerticalAspectRatio = 1.45;

    public static bool ShouldRotate(
        IReadOnlyList<DetectedTextGeometry> blocks,
        int imageWidth,
        int imageHeight)
    {
        if (blocks.Count == 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return false;
        }

        var minimumHeight = Math.Max(
            20,
            Math.Min(imageWidth, imageHeight) * 0.02);
        var vertical = blocks
            .Select(block => new
            {
                block.Text,
                Width = EdgeLength(block.Points, 0, 1, 3, 2),
                Height = EdgeLength(block.Points, 0, 3, 1, 2)
            })
            .Where(block =>
                block.Height >= minimumHeight &&
                block.Height >= block.Width * MinimumVerticalAspectRatio &&
                block.Text.Any(IsKana))
            .ToArray();
        if (vertical.Length == 0)
        {
            return false;
        }

        var japaneseCharacters = vertical.Sum(block =>
            block.Text.Count(character => IsKana(character) || IsHan(character)));
        return japaneseCharacters >= 4 &&
               (vertical.Length >= 2 ||
                vertical.Any(block => block.Height >= block.Width * 2.2));
    }

    private static double EdgeLength(
        IReadOnlyList<SKPointI> points,
        int firstStart,
        int firstEnd,
        int secondStart,
        int secondEnd)
    {
        if (points.Count < 4)
        {
            return 0;
        }

        return (Distance(points[firstStart], points[firstEnd]) +
                Distance(points[secondStart], points[secondEnd])) / 2;
    }

    private static double Distance(SKPointI first, SKPointI second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    private static bool IsKana(char value) =>
        value is >= '\u3040' and <= '\u30ff' or
            >= '\uff66' and <= '\uff9f';

    private static bool IsHan(char value) =>
        value is >= '\u3400' and <= '\u9fff' or
            >= '\uf900' and <= '\ufaff';
}

internal static class OcrBitmapRotation
{
    public static SKBitmap CounterClockwise(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var rotated = new SKBitmap(
            source.Height,
            source.Width,
            source.ColorType,
            source.AlphaType);
        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(0, source.Width);
        canvas.RotateDegrees(-90);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return rotated;
    }
}
