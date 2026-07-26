using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace Sentory.Platform.Windows.Interop;

public static class ClipboardImageDataComposer
{
    internal const string OriginalImageDataFormat =
        "Sentory.ImageContent.v1";

    private const int MaximumExtensionBytes = 16;
    private const int MaximumFileNameBytes = 1024;
    private const int HeaderSize = 15;
    private const int MaximumPayloadBytes =
        (int)ClipboardImageCodec.MaximumEncodedImageBytes +
        HeaderSize + MaximumExtensionBytes + MaximumFileNameBytes;

    private static readonly byte[] Magic = "SENTRYI1"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static System.Windows.DataObject? TryCreate(
        string path,
        string? originalFileName = null)
    {
        var original = ClipboardImageCodec.TryReadFile(path);
        if (original is null)
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(originalFileName))
            {
                original = original with
                {
                    OriginalFileName = Path.GetFileName(originalFileName)
                };
            }

            var bitmap = LoadBitmap(path);
            var data = new System.Windows.DataObject();
            data.SetImage(bitmap);
            data.SetData(
                OriginalImageDataFormat,
                Serialize(original),
                autoConvert: false);
            return data;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  NotSupportedException or FileFormatException or
                  ArgumentException or InvalidDataException)
        {
            return null;
        }
    }

    internal static ClipboardImageSnapshot? TryReadOriginal(
        System.Windows.IDataObject? data)
    {
        if (data is null ||
            !data.GetDataPresent(OriginalImageDataFormat, autoConvert: false))
        {
            return null;
        }

        try
        {
            var payload = ReadPayload(data.GetData(
                OriginalImageDataFormat,
                autoConvert: false));
            if (payload is null || !TryParse(
                    payload,
                    out var content,
                    out var extension,
                    out var originalFileName))
            {
                return null;
            }

            var decoded = ClipboardImageCodec.TryDecode(
                content,
                extension,
                mimeType: null);
            return decoded is null
                ? null
                : decoded with { OriginalFileName = originalFileName };
        }
        catch (Exception exception)
            when (exception is IOException or NotSupportedException or
                  ArgumentException or DecoderFallbackException)
        {
            return null;
        }
    }

    private static BitmapSource LoadBitmap(string path)
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
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("Image has no decodable frame.");
        }

        var bitmap = decoder.Frames[0];
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] Serialize(ClipboardImageSnapshot image)
    {
        var extension = StrictUtf8.GetBytes(image.FileExtension);
        var originalFileName = string.IsNullOrWhiteSpace(image.OriginalFileName)
            ? []
            : StrictUtf8.GetBytes(Path.GetFileName(image.OriginalFileName));
        if (extension.Length is 0 or > MaximumExtensionBytes ||
            originalFileName.Length > MaximumFileNameBytes)
        {
            throw new InvalidDataException("Image clipboard metadata is too large.");
        }

        var payloadLength = checked(
            HeaderSize + extension.Length + originalFileName.Length +
            image.ContentBytes.Length);
        if (payloadLength > MaximumPayloadBytes)
        {
            throw new InvalidDataException("Image clipboard payload is too large.");
        }

        var payload = GC.AllocateUninitializedArray<byte>(payloadLength);
        Magic.CopyTo(payload, 0);
        payload[Magic.Length] = checked((byte)extension.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(Magic.Length + 1, sizeof(ushort)),
            checked((ushort)originalFileName.Length));
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(Magic.Length + 3, sizeof(int)),
            image.ContentBytes.Length);
        var offset = HeaderSize;
        extension.CopyTo(payload, offset);
        offset += extension.Length;
        originalFileName.CopyTo(payload, offset);
        offset += originalFileName.Length;
        image.ContentBytes.CopyTo(payload, offset);
        return payload;
    }

    private static byte[]? ReadPayload(object? value)
    {
        if (value is byte[] bytes)
        {
            return bytes.Length <= MaximumPayloadBytes ? bytes : null;
        }

        if (value is not Stream stream || !stream.CanRead ||
            !stream.CanSeek || stream.Length is <= 0 or > MaximumPayloadBytes)
        {
            return null;
        }

        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            var result = GC.AllocateUninitializedArray<byte>((int)stream.Length);
            stream.ReadExactly(result);
            return result;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static bool TryParse(
        byte[] payload,
        out byte[] content,
        out string extension,
        out string? originalFileName)
    {
        content = [];
        extension = string.Empty;
        originalFileName = null;
        if (payload.Length < HeaderSize ||
            !payload.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            return false;
        }

        var extensionLength = payload[Magic.Length];
        var fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(
            payload.AsSpan(Magic.Length + 1, sizeof(ushort)));
        var contentLength = BinaryPrimitives.ReadInt32LittleEndian(
            payload.AsSpan(Magic.Length + 3, sizeof(int)));
        if (extensionLength is 0 or > MaximumExtensionBytes ||
            fileNameLength > MaximumFileNameBytes ||
            contentLength <= 0 ||
            contentLength > ClipboardImageCodec.MaximumEncodedImageBytes)
        {
            return false;
        }

        var expectedLength = (long)HeaderSize + extensionLength +
                             fileNameLength + contentLength;
        if (expectedLength != payload.Length)
        {
            return false;
        }

        var offset = HeaderSize;
        extension = StrictUtf8.GetString(
            payload,
            offset,
            extensionLength);
        offset += extensionLength;
        if (fileNameLength > 0)
        {
            originalFileName = Path.GetFileName(StrictUtf8.GetString(
                payload,
                offset,
                fileNameLength));
        }

        offset += fileNameLength;
        content = payload.AsSpan(offset, contentLength).ToArray();
        return true;
    }
}
