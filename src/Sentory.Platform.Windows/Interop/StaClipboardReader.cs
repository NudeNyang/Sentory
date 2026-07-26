using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfTextDataFormat = System.Windows.TextDataFormat;
using WpfClipboard = System.Windows.Clipboard;

namespace Sentory.Platform.Windows.Interop;

public sealed record ClipboardImageSnapshot(
    byte[] ContentBytes,
    string Sha256,
    int PixelWidth,
    int PixelHeight,
    string MimeType,
    string FileExtension,
    string? OriginalFileName = null);

public sealed record ClipboardSnapshot(
    uint SequenceNumber,
    string? Text,
    IReadOnlyList<ClipboardImageSnapshot> Images)
{
    public ClipboardImageSnapshot? Image => Images.FirstOrDefault();
}

public sealed class StaClipboardReader : IDisposable
{
    private readonly INativeWindowApi _native;
    private readonly BlockingCollection<ReadRequest> _requests = [];
    private readonly Thread _thread;
    private bool _disposed;

    public StaClipboardReader(INativeWindowApi native)
    {
        _native = native;
        _thread = new Thread(ProcessRequests)
        {
            IsBackground = true,
            Name = "Sentory Clipboard Reader"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<ClipboardSnapshot?> ReadAsync(
        uint expectedSequenceNumber,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var completion =
            new TaskCompletionSource<ClipboardSnapshot?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ReadRequest(
            expectedSequenceNumber,
            completion,
            cancellationToken);
        _requests.Add(request, cancellationToken);
        return completion.Task;
    }

    private void ProcessRequests()
    {
        foreach (var request in _requests.GetConsumingEnumerable())
        {
            if (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(request.CancellationToken);
                continue;
            }

            try
            {
                request.Completion.TrySetResult(
                    ReadClipboard(request.ExpectedSequence));
            }
            catch (Exception exception)
            {
                request.Completion.TrySetException(exception);
            }
        }
    }

    private ClipboardSnapshot? ReadClipboard(uint expectedSequence)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (_native.GetClipboardSequenceNumber() != expectedSequence)
            {
                return null;
            }

            try
            {
                var sentoryImage = ClipboardImageDataComposer.TryReadOriginal(
                    WpfClipboard.GetDataObject());
                var imagePaths = sentoryImage is null &&
                                 WpfClipboard.ContainsFileDropList()
                    ? WpfClipboard.GetFileDropList()
                        .Cast<string>()
                        .Where(ClipboardImageCodec.IsSupportedImagePath)
                        .ToArray()
                    : [];
                BitmapSource? bitmap = null;
                if (sentoryImage is null && imagePaths.Length == 0 &&
                    WpfClipboard.ContainsImage())
                {
                    bitmap = WpfClipboard.GetImage();
                    bitmap?.Freeze();
                }

                var text = WpfClipboard.ContainsText(
                    WpfTextDataFormat.UnicodeText)
                    ? WpfClipboard.GetText(WpfTextDataFormat.UnicodeText)
                    : null;
                var sequenceAfterRead = _native.GetClipboardSequenceNumber();
                if (sequenceAfterRead != expectedSequence)
                {
                    return null;
                }

                // The clipboard-owned data is detached now. File reads and PNG
                // encoding can be slow for large images, so do them only after
                // the sequence-stability check instead of turning that work into
                // a race against the messenger.
                IReadOnlyList<ClipboardImageSnapshot> images =
                    sentoryImage is not null
                        ? [sentoryImage]
                        : imagePaths.Length > 0
                            ? ClipboardImageCodec.TryReadFiles(imagePaths)
                            : bitmap is { PixelWidth: > 0, PixelHeight: > 0 }
                                ? ClipboardImageCodec.TryEncode(bitmap) is
                                { } encoded
                                    ? [encoded]
                                    : []
                                : [];
                if (images.Count == 0 && string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                return new ClipboardSnapshot(
                    sequenceAfterRead,
                    string.IsNullOrWhiteSpace(text) ? null : text,
                    images);
            }
            catch (COMException) when (attempt < 4)
            {
                Thread.Sleep(20);
            }
            catch (ExternalException) when (attempt < 4)
            {
                Thread.Sleep(20);
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _requests.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(1));
        _requests.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record ReadRequest(
        uint ExpectedSequence,
        TaskCompletionSource<ClipboardSnapshot?> Completion,
        CancellationToken CancellationToken);
}

internal static class ClipboardImageCodec
{
    internal const long MaximumEncodedImageBytes = 64L * 1024 * 1024;
    internal const long MaximumBatchImageBytes = 256L * 1024 * 1024;
    internal const int MaximumImagesPerBatch = 12;
    internal const long MaximumPixelCount = 60_000_000;
    internal const int MaximumDimension = 32_768;

    private static readonly HashSet<string> SupportedExtensions = new(
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsSupportedImagePath(string path) =>
        File.Exists(path) && HasSupportedImageExtension(path);

    public static bool HasSupportedImageExtension(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    public static IEnumerable<string> EnumerateSupportedExtensions() =>
        SupportedExtensions;

    public static ClipboardImageSnapshot? TryReadFile(string path)
    {
        try
        {
            byte[] bytes;
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is <= 0 or > MaximumEncodedImageBytes)
            {
                return null;
            }

            bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
            stream.ReadExactly(bytes);

            var extension = NormalizeExtension(Path.GetExtension(path));
            var decoded = TryDecode(
                bytes,
                extension,
                MimeTypeFor(extension));
            return decoded is null
                ? null
                : decoded with { OriginalFileName = Path.GetFileName(path) };
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    public static IReadOnlyList<ClipboardImageSnapshot> TryReadFiles(
        IEnumerable<string> paths)
    {
        var images = new List<ClipboardImageSnapshot>();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var path in paths
                     .Where(IsSupportedImagePath)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumImagesPerBatch))
        {
            var image = TryReadFile(path);
            if (image is null || !hashes.Add(image.Sha256))
            {
                continue;
            }

            if (image.ContentBytes.LongLength >
                MaximumBatchImageBytes - totalBytes)
            {
                continue;
            }

            images.Add(image);
            totalBytes += image.ContentBytes.LongLength;
        }

        return images;
    }

    public static ClipboardImageSnapshot? TryEncode(BitmapSource bitmap)
    {
        if (!IsAllowedDimensions(bitmap.PixelWidth, bitmap.PixelHeight))
        {
            return null;
        }

        try
        {
            return Encode(bitmap);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public static ClipboardImageSnapshot Encode(BitmapSource bitmap)
    {
        if (!IsAllowedDimensions(bitmap.PixelWidth, bitmap.PixelHeight))
        {
            throw new InvalidDataException("Image dimensions exceed the capture limit.");
        }

        bitmap = RepairMissingClipboardAlpha(bitmap);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var bytes = stream.ToArray();
        if (bytes.LongLength > MaximumEncodedImageBytes)
        {
            throw new InvalidDataException("Encoded image exceeds the capture limit.");
        }

        return new ClipboardImageSnapshot(
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes)),
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            "image/png",
            ".png");
    }

    private static BitmapSource RepairMissingClipboardAlpha(BitmapSource bitmap)
    {
        if (bitmap.Format != PixelFormats.Bgra32 &&
            bitmap.Format != PixelFormats.Pbgra32)
        {
            return bitmap;
        }

        var stride = checked(bitmap.PixelWidth * 4);
        var pixels = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(pixels, stride, 0);

        var hasVisibleColor = false;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] != 0)
            {
                return bitmap;
            }

            hasVisibleColor |= pixels[offset] != 0 ||
                               pixels[offset + 1] != 0 ||
                               pixels[offset + 2] != 0;
        }

        if (!hasVisibleColor)
        {
            return bitmap;
        }

        for (var offset = 3; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = byte.MaxValue;
        }

        var repaired = BitmapSource.Create(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            bitmap.DpiX,
            bitmap.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        repaired.Freeze();
        return repaired;
    }

    public static ClipboardImageSnapshot? TryDecode(
        byte[] bytes,
        string? fileExtension,
        string? mimeType)
    {
        try
        {
            if (bytes.LongLength is <= 0 or > MaximumEncodedImageBytes)
            {
                return null;
            }

            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat |
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            if (decoder.Frames.Count == 0)
            {
                return null;
            }

            var frame = decoder.Frames[0];
            if (!IsAllowedDimensions(frame.PixelWidth, frame.PixelHeight))
            {
                return null;
            }

            var extension = NormalizeExtension(
                ResolveExtension(fileExtension, mimeType));
            if (!SupportedExtensions.Contains(extension))
            {
                return null;
            }

            return new ClipboardImageSnapshot(
                bytes,
                Convert.ToHexString(SHA256.HashData(bytes)),
                frame.PixelWidth,
                frame.PixelHeight,
                MimeTypeFor(extension),
                extension);
        }
        catch (Exception exception)
            when (exception is NotSupportedException or FileFormatException or
                  ArgumentException)
        {
            return null;
        }
    }

    internal static bool IsAllowedDimensions(int width, int height) =>
        width is > 0 and <= MaximumDimension &&
        height is > 0 and <= MaximumDimension &&
        (long)width * height <= MaximumPixelCount;

    private static string ResolveExtension(
        string? extension,
        string? mimeType)
    {
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.StartsWith('.') ? extension : $".{extension}";
        }

        return mimeType?.Split(';', 2)[0].Trim().ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/bmp" => ".bmp",
            "image/gif" => ".gif",
            "image/tiff" => ".tif",
            "image/webp" => ".webp",
            _ => ".png"
        };
    }

    private static string NormalizeExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".jpeg" => ".jpg",
            ".tiff" => ".tif",
            var value => value
        };

    private static string MimeTypeFor(string extension) => extension switch
    {
        ".jpg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        ".tif" => "image/tiff",
        ".webp" => "image/webp",
        _ => "image/png"
    };
}
