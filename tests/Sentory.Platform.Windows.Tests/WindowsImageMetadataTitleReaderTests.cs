using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sentory.Platform.Windows.Ocr;
using SkiaSharp;

namespace Sentory.Platform.Windows.Tests;

public sealed class WindowsImageMetadataTitleReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Metadata.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReadsEmbeddedJpegTitle()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "hashed-name.jpg");
        var pixels = new byte[4 * 4 * 3];
        var bitmap = BitmapSource.Create(
            4,
            4,
            96,
            96,
            PixelFormats.Bgr24,
            null,
            pixels,
            4 * 3);
        var metadata = new BitmapMetadata("jpg")
        {
            Title = "여름 바다 여행"
        };
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        var title = new WindowsImageMetadataTitleReader().ReadTitle(path);

        Assert.Equal("여름 바다 여행", title);
    }

    [Fact]
    public void ReadsEmbeddedPngTitle()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "hashed-name.png");
        var pixels = new byte[4 * 4 * 4];
        var bitmap = BitmapSource.Create(
            4,
            4,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            4 * 4);
        var metadata = new BitmapMetadata("png");
        metadata.SetQuery("/tEXt/{str=Title}", "캐릭터 설정화");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        var title = new WindowsImageMetadataTitleReader().ReadTitle(path);

        Assert.Equal("캐릭터 설정화", title);
    }

    [Fact]
    public void RejectsBlocksBelowOnePercentOfShortImageSide()
    {
        SKPointI[] tiny =
        [
            new(0, 0), new(100, 0), new(100, 6), new(0, 6)
        ];
        SKPointI[] readable =
        [
            new(0, 0), new(100, 0), new(100, 14), new(0, 14)
        ];

        Assert.False(OcrTextBlockFilter.IsReadable(tiny, 2000, 1000));
        Assert.True(OcrTextBlockFilter.IsReadable(readable, 2000, 1000));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
