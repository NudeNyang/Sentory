using Sentory.Platform.Windows.Ocr;
using SkiaSharp;

namespace Sentory.Platform.Windows.Tests;

public sealed class WindowsImageTextRecognizerTests
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2F1kAAAAASUVORK5CYII=";

    [Fact]
    public async Task DecodesPngWhenWindowsOcrLanguageIsAvailable()
    {
        var recognizer = new WindowsImageTextRecognizer();
        if (!recognizer.IsAvailable)
        {
            return;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"sentory-ocr-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(
            path,
            Convert.FromBase64String(OnePixelPng));
        try
        {
            var result = await recognizer.RecognizeAsync(path);

            Assert.Equal("Windows.Media.Ocr", result.EngineName);
            Assert.NotNull(result.Lines);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PaddleOcrLoadsEmbeddedModelsAndDecodesPng()
    {
        var cacheDirectory = Path.Combine(
            Path.GetTempPath(),
            $"sentory-paddle-ocr-{Guid.NewGuid():N}");
        var path = Path.Combine(cacheDirectory, "one-pixel.png");
        Directory.CreateDirectory(cacheDirectory);
        using (var bitmap = new SKBitmap(64, 64))
        {
            bitmap.Erase(SKColors.White);
            using var output = File.Create(path);
            bitmap.Encode(output, SKEncodedImageFormat.Png, 100);
        }
        try
        {
            using var recognizer = new PaddleOcrImageTextRecognizer(
                Path.Combine(cacheDirectory, "models"));

            var result = await recognizer.RecognizeAsync(path);

            Assert.Equal(
                PaddleOcrImageTextRecognizer.PaddleEngineName,
                result.EngineName);
            Assert.NotNull(result.Lines);
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }
}
