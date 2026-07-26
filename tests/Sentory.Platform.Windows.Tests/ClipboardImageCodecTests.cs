using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class ClipboardImageCodecTests
{
    [Fact]
    public void SentoryClipboardDataKeepsOriginalImageBytesAlongsideBitmap()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"sentory-clipboard-original-{Guid.NewGuid():N}.png");
        try
        {
            var bitmap = BitmapSource.Create(
                2,
                1,
                144,
                144,
                PixelFormats.Bgra32,
                null,
                new byte[]
                {
                    255, 0, 0, 255,
                    0, 255, 0, 255
                },
                8);
            var metadata = new BitmapMetadata("png");
            metadata.SetQuery("/tEXt/{str=Description}", "Sentory original metadata");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            var originalBytes = File.ReadAllBytes(path);
            var data = ClipboardImageDataComposer.TryCreate(path);
            var restored = ClipboardImageDataComposer.TryReadOriginal(data);

            Assert.NotNull(data);
            Assert.NotNull(data.GetImage());
            Assert.NotNull(restored);
            Assert.Equal(originalBytes, restored.ContentBytes);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(originalBytes)),
                restored.Sha256);
            Assert.Equal(Path.GetFileName(path), restored.OriginalFileName);
            Assert.Equal(".png", restored.FileExtension);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SentoryClipboardDataUsesTheGalleryOriginalFileName()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"{new string('a', 64)}.png");
        const string originalFileName =
            "VRChat 2026-07-26 23-18-47.png";
        try
        {
            var bitmap = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                new byte[] { 255, 0, 0, 255 },
                4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            var data = ClipboardImageDataComposer.TryCreate(
                path,
                originalFileName);
            var restored = ClipboardImageDataComposer.TryReadOriginal(data);

            Assert.NotNull(restored);
            Assert.Equal(originalFileName, restored.OriginalFileName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsMalformedSentoryClipboardImageData()
    {
        var data = new System.Windows.DataObject();
        data.SetData(
            ClipboardImageDataComposer.OriginalImageDataFormat,
            new byte[] { 1, 2, 3 },
            autoConvert: false);

        var restored = ClipboardImageDataComposer.TryReadOriginal(data);

        Assert.Null(restored);
    }

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
    public void RepairsClipboardBitmapWhoseUnusedAlphaByteIsZero()
    {
        var bitmap = BitmapSource.Create(
            2,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[]
            {
                255, 0, 0, 0,
                0, 255, 0, 0
            },
            8);

        var snapshot = ClipboardImageCodec.Encode(bitmap);
        var pixels = DecodeBgra32(snapshot.ContentBytes, 2, 1);

        Assert.Equal(
            new byte[]
            {
                255, 0, 0, 255,
                0, 255, 0, 255
            },
            pixels);
    }

    [Fact]
    public void PreservesClipboardBitmapWithRealTransparency()
    {
        var sourcePixels = new byte[]
        {
            20, 10, 5, 0,
            60, 40, 20, 128
        };
        var bitmap = BitmapSource.Create(
            2,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            sourcePixels,
            8);

        var snapshot = ClipboardImageCodec.Encode(bitmap);
        var pixels = DecodeBgra32(snapshot.ContentBytes, 2, 1);

        Assert.Equal(sourcePixels, pixels);
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

    [Fact]
    public void RejectsImageFileLargerThanEncodedByteLimitBeforeReadingIt()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"sentory-oversized-{Guid.NewGuid():N}.png");
        try
        {
            using (var stream = File.Create(path))
            {
                stream.SetLength(ClipboardImageCodec.MaximumEncodedImageBytes + 1);
            }

            Assert.Null(ClipboardImageCodec.TryReadFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AllowsEightKButRejectsExcessiveDecodedPixelCount()
    {
        Assert.True(ClipboardImageCodec.IsAllowedDimensions(7680, 4320));
        Assert.False(ClipboardImageCodec.IsAllowedDimensions(10_000, 10_000));
        Assert.False(ClipboardImageCodec.IsAllowedDimensions(40_000, 1));
    }

    private static byte[] DecodeBgra32(byte[] bytes, int width, int height)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        BitmapSource converted = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
