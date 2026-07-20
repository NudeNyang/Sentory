using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class ClipboardImageCodecTests
{
    [Fact]
    public void EncodesImageLargerThanEightMegabytes()
    {
        const int width = 2048;
        const int height = 1536;
        const int stride = width * 4;
        var pixels = RandomNumberGenerator.GetBytes(stride * height);
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);

        var snapshot = ClipboardImageCodec.Encode(bitmap);

        Assert.True(snapshot.ContentBytes.Length > 8 * 1024 * 1024);
        Assert.Equal(width, snapshot.PixelWidth);
        Assert.Equal(height, snapshot.PixelHeight);
        Assert.Equal(
            snapshot.Sha256,
            Convert.ToHexString(SHA256.HashData(snapshot.ContentBytes)));
        Assert.Equal("image/png", snapshot.MimeType);
        Assert.Equal(".png", snapshot.FileExtension);
    }

    [Fact]
    public void ReadsLargeJpegWithoutExpandingOrChangingItsBytes()
    {
        const int width = 3072;
        const int height = 3072;
        const int stride = width * 4;
        var pixels = RandomNumberGenerator.GetBytes(stride * height);
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(Path.GetTempPath(), $"sentory-large-{Guid.NewGuid():N}.jpg");
        try
        {
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            var original = File.ReadAllBytes(path);
            Assert.True(original.Length > 8 * 1024 * 1024);

            var snapshot = ClipboardImageCodec.TryReadFile(path);

            Assert.NotNull(snapshot);
            Assert.Equal(original, snapshot.ContentBytes);
            Assert.Equal("image/jpeg", snapshot.MimeType);
            Assert.Equal(".jpg", snapshot.FileExtension);
            Assert.Equal(Path.GetFileName(path), snapshot.OriginalFileName);
            Assert.Equal(width, snapshot.PixelWidth);
            Assert.Equal(height, snapshot.PixelHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
