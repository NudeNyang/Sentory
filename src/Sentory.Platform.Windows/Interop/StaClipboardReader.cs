using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
    string FileExtension);

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
                var imagePaths = WpfClipboard.ContainsFileDropList()
                    ? WpfClipboard.GetFileDropList()
                        .Cast<string>()
                        .Where(ClipboardImageCodec.IsSupportedImagePath)
                        .ToArray()
                    : [];
                BitmapSource? bitmap = null;
                if (imagePaths.Length == 0 && WpfClipboard.ContainsImage())
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
                IReadOnlyList<ClipboardImageSnapshot> images = imagePaths.Length > 0
                    ? imagePaths
                        .Select(ClipboardImageCodec.TryReadFile)
                        .Where(image => image is not null)
                        .Cast<ClipboardImageSnapshot>()
                        .ToList()
                    : bitmap is { PixelWidth: > 0, PixelHeight: > 0 }
                        ? [ClipboardImageCodec.Encode(bitmap)]
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
    private static readonly HashSet<string> SupportedExtensions = new(
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsSupportedImagePath(string path) =>
        File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path));

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
            using (var content = new MemoryStream(
                       stream.Length > int.MaxValue ? 0 : (int)stream.Length))
            {
                stream.CopyTo(content);
                bytes = content.ToArray();
            }

            var extension = NormalizeExtension(Path.GetExtension(path));
            return TryDecode(
                bytes,
                extension,
                MimeTypeFor(extension));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    public static ClipboardImageSnapshot Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var bytes = stream.ToArray();
        return new ClipboardImageSnapshot(
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes)),
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            "image/png",
            ".png");
    }

    public static ClipboardImageSnapshot? TryDecode(
        byte[] bytes,
        string? fileExtension,
        string? mimeType)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
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
                decoder.Frames[0].PixelWidth,
                decoder.Frames[0].PixelHeight,
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
