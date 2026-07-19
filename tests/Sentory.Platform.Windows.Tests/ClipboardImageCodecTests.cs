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

        Assert.True(snapshot.PngBytes.Length > 8 * 1024 * 1024);
        Assert.Equal(width, snapshot.PixelWidth);
        Assert.Equal(height, snapshot.PixelHeight);
        Assert.Equal(
            snapshot.Sha256,
            Convert.ToHexString(SHA256.HashData(snapshot.PngBytes)));
    }
}
