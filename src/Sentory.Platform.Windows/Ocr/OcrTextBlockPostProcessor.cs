using System.Text;
using System.Globalization;
using SkiaSharp;

namespace Sentory.Platform.Windows.Ocr;

internal sealed record OcrDetectedTextBlock(
    string Text,
    IReadOnlyList<SKPointI> Points,
    double Confidence);

internal sealed record ProcessedOcrTextBlocks(
    IReadOnlyList<OcrDetectedTextBlock> Blocks,
    IReadOnlyList<string> PreferredTitleLines);

internal static class OcrTextBlockPostProcessor
{
    private const int RepeatedOverlayMinimumCount = 3;
    private const double RepeatedOverlayMinimumSpan = 0.3;
    private const double HeadingRelativeHeight = 0.6;
    private const double HeadingMinimumImageRatio = 0.015;

    public static ProcessedOcrTextBlocks Process(
        IReadOnlyList<OcrDetectedTextBlock> blocks,
        int imageWidth,
        int imageHeight,
        bool joinVerticalColumns)
    {
        var cleanedBlocks = blocks
            .Select(block => block with
            {
                Text = RemoveIconLikeJamoPrefix(block.Text)
            })
            .ToArray();
        var suppressedKeys = FindRepeatedOverlayKeys(
            cleanedBlocks,
            imageWidth,
            imageHeight);
        var retained = cleanedBlocks
            .Where(block =>
                IsMeaningfulText(block.Text) &&
                !suppressedKeys.Contains(Canonicalize(block.Text)))
            .ToArray();
        var preferred = SelectPreferredTitleLines(
            retained,
            imageWidth,
            imageHeight,
            joinVerticalColumns);
        return new ProcessedOcrTextBlocks(retained, preferred);
    }

    private static string RemoveIconLikeJamoPrefix(string text)
    {
        if (text.Length < 2)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            var followsWordCharacter = index > 0 &&
                (char.IsLetterOrDigit(text[index - 1]) ||
                 IsHangulCompatibilityJamo(text[index - 1]));
            var precedesLatinWord = index + 1 < text.Length &&
                IsAsciiLetter(text[index + 1]);
            if (IsHangulCompatibilityJamo(character) &&
                !followsWordCharacter &&
                precedesLatinWord)
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool IsHangulCompatibilityJamo(char character) =>
        character is >= '\u3130' and <= '\u318f';

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsMeaningfulText(string text)
    {
        var letterOrDigitCount = text.Count(char.IsLetterOrDigit);
        if (letterOrDigitCount >= 2)
        {
            return true;
        }

        return text.Any(character =>
            char.GetUnicodeCategory(character) == UnicodeCategory.OtherLetter);
    }

    private static HashSet<string> FindRepeatedOverlayKeys(
        IReadOnlyList<OcrDetectedTextBlock> blocks,
        int imageWidth,
        int imageHeight)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return keys;
        }

        foreach (var group in blocks
                     .Select(block => (Block: block, Key: Canonicalize(block.Text)))
                     .Where(value => value.Key.Length >= 5)
                     .GroupBy(value => value.Key, StringComparer.Ordinal)
                     .Where(group => group.Count() >= RepeatedOverlayMinimumCount))
        {
            var centers = group
                .Select(value => Center(value.Block.Points))
                .ToArray();
            var horizontalSpan = centers.Max(point => point.X) -
                                 centers.Min(point => point.X);
            var verticalSpan = centers.Max(point => point.Y) -
                               centers.Min(point => point.Y);
            if (horizontalSpan >= imageWidth * RepeatedOverlayMinimumSpan ||
                verticalSpan >= imageHeight * RepeatedOverlayMinimumSpan)
            {
                keys.Add(group.Key);
            }
        }

        return keys;
    }

    private static IReadOnlyList<string> SelectPreferredTitleLines(
        IReadOnlyList<OcrDetectedTextBlock> blocks,
        int imageWidth,
        int imageHeight,
        bool joinVerticalColumns)
    {
        var horizontal = blocks
            .Select(block => new
            {
                Block = block,
                Width = EdgeLength(block.Points, 0, 1, 3, 2),
                Height = EdgeLength(block.Points, 0, 3, 1, 2)
            })
            .Where(value =>
                value.Block.Confidence >= 0.55 &&
                value.Block.Text.Count(char.IsLetterOrDigit) >= 3 &&
                value.Width >= value.Height * 1.25)
            .ToArray();
        if (horizontal.Length == 0)
        {
            return [];
        }

        var maximumHeight = horizontal.Max(value => value.Height);
        var minimumHeight = Math.Max(
            maximumHeight * HeadingRelativeHeight,
            Math.Min(imageWidth, imageHeight) * HeadingMinimumImageRatio);
        var significant = horizontal
            .Where(value => value.Height >= minimumHeight)
            .Select(value => value.Block.Text.Trim())
            .Where(text => text.Length > 0)
            .Take(6)
            .ToArray();
        if (!joinVerticalColumns || significant.Length <= 1)
        {
            return significant;
        }

        if (significant[0].Contains('「') || significant[0].Contains('『'))
        {
            return significant;
        }

        return [string.Concat(significant)];
    }

    private static string Canonicalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        if (builder.Length >= 5 &&
            builder[0] is 'Q' or 'O' or 'D' or '0' &&
            builder.ToString(1, builder.Length - 1).Any(char.IsDigit))
        {
            builder[0] = '0';
        }

        return builder.ToString();
    }

    private static SKPoint Center(IReadOnlyList<SKPointI> points)
    {
        if (points.Count == 0)
        {
            return SKPoint.Empty;
        }

        return new SKPoint(
            (float)points.Average(point => point.X),
            (float)points.Average(point => point.Y));
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
}
