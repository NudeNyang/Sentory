using Sentory.Platform.Windows.Ocr;
using SkiaSharp;

namespace Sentory.Platform.Windows.Tests;

public sealed class OcrTextBlockPostProcessorTests
{
    [Fact]
    public void RemovesRepeatedWatermarkVariantsSpreadAcrossImage()
    {
        OcrDetectedTextBlock[] blocks =
        [
            Block("Q123RF°", 20, 20, 180, 55),
            Block("0123RF", 700, 30, 180, 55),
            Block("O123RF", 30, 600, 180, 55),
            Block("Q123RF", 700, 620, 180, 55)
        ];

        var processed = OcrTextBlockPostProcessor.Process(
            blocks,
            950,
            1300,
            joinVerticalColumns: false);

        Assert.Empty(processed.Blocks);
        Assert.Empty(processed.PreferredTitleLines);
    }

    [Fact]
    public void JoinsLargeCorrectedVerticalColumnsAndIgnoresSmallParagraphs()
    {
        OcrDetectedTextBlock[] blocks =
        [
            Block("のび太くん", 10, 20, 319, 75, 1),
            Block("ネズミは", 10, 110, 246, 83, 1),
            Block("もういない？", 10, 210, 337, 70, 0.98),
            Block("2年かけた方がしいじゃありませんか", 10, 320, 157, 18, 0.89)
        ];

        var processed = OcrTextBlockPostProcessor.Process(
            blocks,
            660,
            1000,
            joinVerticalColumns: true);

        Assert.Equal(4, processed.Blocks.Count);
        Assert.Equal(
            ["のび太くんネズミはもういない？"],
            processed.PreferredTitleLines);
    }

    [Fact]
    public void KeepsLargestHorizontalHeadingAsNormalTitleCandidate()
    {
        OcrDetectedTextBlock[] blocks =
        [
            Block("프로젝트 일정", 10, 20, 300, 60, 0.98),
            Block("회의는 오후 세 시", 10, 100, 260, 22, 0.96)
        ];

        var processed = OcrTextBlockPostProcessor.Process(
            blocks,
            1000,
            700,
            joinVerticalColumns: false);

        Assert.Equal(["프로젝트 일정"], processed.PreferredTitleLines);
    }

    [Fact]
    public void RemovesSymbolOnlyAndSingleLatinCharacterNoise()
    {
        OcrDetectedTextBlock[] blocks =
        [
            Block("□□", 10, 10, 140, 60),
            Block("●", 10, 80, 80, 60),
            Block("□←→", 10, 150, 180, 60),
            Block("0-", 10, 220, 80, 60),
            Block("e", 10, 290, 60, 60),
            Block("追踪哈兰德世界杯话题", 10, 360, 400, 60)
        ];

        var processed = OcrTextBlockPostProcessor.Process(
            blocks,
            1000,
            700,
            joinVerticalColumns: false);

        Assert.Equal(
            ["追踪哈兰德世界杯话题"],
            processed.Blocks.Select(block => block.Text));
    }

    [Fact]
    public void RemovesAnIconMisreadAsSingleJamoBeforeALatinWord()
    {
        OcrDetectedTextBlock[] blocks =
        [
            Block(
                "실행은 기존 ㄷDiscord 번역 오버레이 바로가기를 더블클릭하면 돼.",
                10,
                20,
                900,
                55),
            Block("ㄷㄷ Discord가 다시 켜졌네", 10, 90, 500, 55),
            Block("키보드에서 ㄷ 키를 누르세요", 10, 160, 500, 55),
            Block("C# · C++ · ©2026", 10, 230, 500, 55)
        ];

        var processed = OcrTextBlockPostProcessor.Process(
            blocks,
            1000,
            700,
            joinVerticalColumns: false);

        Assert.Equal(
            [
                "실행은 기존 Discord 번역 오버레이 바로가기를 더블클릭하면 돼.",
                "ㄷㄷ Discord가 다시 켜졌네",
                "키보드에서 ㄷ 키를 누르세요",
                "C# · C++ · ©2026"
            ],
            processed.Blocks.Select(block => block.Text));
    }

    private static OcrDetectedTextBlock Block(
        string text,
        int x,
        int y,
        int width,
        int height,
        double confidence = 0.9) =>
        new(
            text,
            [
                new(x, y),
                new(x + width, y),
                new(x + width, y + height),
                new(x, y + height)
            ],
            confidence);
}
