using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sentory.App.Tests;

public sealed class GalleryCardThumbnailStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sentory-card-thumbnail-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreatesAndReusesDisplaySizedThumbnail()
    {
        Directory.CreateDirectory(_root);
        var originalPath = Path.Combine(_root, "original.png");
        WriteImage(originalPath, 800, 400);
        var store = new GalleryCardThumbnailStore(_root);

        Assert.Null(store.TryGetExisting(originalPath));

        var firstPath = store.GetOrCreate(originalPath);

        firstPath = Assert.IsType<string>(firstPath);
        Assert.True(File.Exists(firstPath));
        using (var stream = File.OpenRead(firstPath))
        {
            var frame = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            Assert.Equal(GalleryArtworkDecodePolicy.CardWidth, frame.PixelWidth);
            Assert.Equal(192, frame.PixelHeight);
        }

        var firstWriteTime = File.GetLastWriteTimeUtc(firstPath);
        var secondPath = store.GetOrCreate(originalPath);

        Assert.Equal(firstPath, secondPath);
        Assert.Equal(
            firstWriteTime,
            File.GetLastWriteTimeUtc(Assert.IsType<string>(secondPath)));
        Assert.Equal(firstPath, store.TryGetExisting(originalPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void WriteImage(string path, int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
