using Sentory.Platform.Windows.Ocr;
using SkiaSharp;

namespace Sentory.Platform.Windows.Tests;

public sealed class VerticalJapaneseTextDetectorTests
{
    [Fact]
    public void DetectsMultipleTallJapaneseTextColumns()
    {
        DetectedTextGeometry[] blocks =
        [
            new("「彼女と", Box(0, 0, 70, 190)),
            new("デートなう」", Box(100, 0, 65, 220)),
            new("English", Box(0, 300, 240, 35))
        ];

        Assert.True(VerticalJapaneseTextDetector.ShouldRotate(blocks, 1400, 2000));
    }

    [Fact]
    public void LeavesHorizontalJapaneseTextUnrotated()
    {
        DetectedTextGeometry[] blocks =
        [
            new("空に広がる物語", Box(0, 0, 360, 55)),
            new("ケセドの", Box(0, 80, 220, 50))
        ];

        Assert.False(VerticalJapaneseTextDetector.ShouldRotate(blocks, 1400, 900));
    }

    [Fact]
    public void CounterClockwiseRotationMakesTopToBottomPixelsLeftToRight()
    {
        using var source = new SKBitmap(2, 3);
        source.SetPixel(0, 0, SKColors.Red);
        source.SetPixel(0, 1, SKColors.Green);
        source.SetPixel(0, 2, SKColors.Blue);

        using var rotated = OcrBitmapRotation.CounterClockwise(source);

        Assert.Equal(3, rotated.Width);
        Assert.Equal(2, rotated.Height);
        Assert.Equal(SKColors.Red, rotated.GetPixel(0, 1));
        Assert.Equal(SKColors.Green, rotated.GetPixel(1, 1));
        Assert.Equal(SKColors.Blue, rotated.GetPixel(2, 1));
    }

    private static SKPointI[] Box(int x, int y, int width, int height) =>
    [
        new(x, y),
        new(x + width, y),
        new(x + width, y + height),
        new(x, y + height)
    ];
}
