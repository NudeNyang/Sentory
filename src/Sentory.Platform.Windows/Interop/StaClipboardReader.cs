using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using WpfTextDataFormat = System.Windows.TextDataFormat;
using WpfClipboard = System.Windows.Clipboard;

namespace Sentory.Platform.Windows.Interop;

public sealed record ClipboardImageSnapshot(
    byte[] PngBytes,
    string Sha256,
    int PixelWidth,
    int PixelHeight);

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
                var images = ReadImages();
                var text = WpfClipboard.ContainsText(
                    WpfTextDataFormat.UnicodeText)
                    ? WpfClipboard.GetText(WpfTextDataFormat.UnicodeText)
                    : null;
                var sequenceAfterRead = _native.GetClipboardSequenceNumber();
                if (sequenceAfterRead != expectedSequence)
                {
                    return null;
                }

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

    private static IReadOnlyList<ClipboardImageSnapshot> ReadImages()
    {
        if (WpfClipboard.ContainsFileDropList())
        {
            var images = WpfClipboard.GetFileDropList()
                .Cast<string>()
                .Where(ClipboardImageCodec.IsSupportedImagePath)
                .Select(ClipboardImageCodec.TryReadFile)
                .Where(image => image is not null)
                .Cast<ClipboardImageSnapshot>()
                .ToList();
            if (images.Count > 0)
            {
                return images;
            }
        }

        if (!WpfClipboard.ContainsImage())
        {
            return [];
        }

        var bitmap = WpfClipboard.GetImage();
        if (bitmap is null ||
            bitmap.PixelWidth <= 0 ||
            bitmap.PixelHeight <= 0)
        {
            return [];
        }

        return [ClipboardImageCodec.Encode(bitmap)];
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
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            return decoder.Frames.Count == 0
                ? null
                : Encode(decoder.Frames[0]);
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
            bitmap.PixelHeight);
    }
}
