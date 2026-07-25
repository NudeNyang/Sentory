using System.Text.Json;

namespace Sentory.Core.Sync;

public static class SyncItemPayloadSerializer
{
    public const int MaximumSerializedBytes = 128 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static byte[] Serialize(SyncItemPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var content = JsonSerializer.SerializeToUtf8Bytes(
            ToDocument(payload),
            JsonOptions);
        if (content.Length > MaximumSerializedBytes)
        {
            throw new InvalidDataException(
                "항목 동기화 본문이 허용된 크기를 넘었습니다.");
        }

        return content;
    }

    public static SyncItemPayload Deserialize(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0 ||
            content.Length > MaximumSerializedBytes)
        {
            throw new InvalidDataException(
                "항목 동기화 본문 크기가 허용 범위를 벗어났습니다.");
        }

        SyncItemPayloadDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SyncItemPayloadDocument>(
                content,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "항목 동기화 JSON을 읽을 수 없습니다.",
                exception);
        }

        if (document is null)
        {
            throw new InvalidDataException(
                "항목 동기화 JSON이 비어 있습니다.");
        }

        try
        {
            return new SyncItemPayload(
                document.PayloadVersion,
                document.ContentKind,
                document.SourceApp,
                document.CaptureMethod,
                document.DeliveryStatus,
                document.ContextHash,
                document.CapturedAt,
                document.ConfirmationSignals,
                document.Url is null
                    ? null
                    : new SyncUrlContent(
                        document.Url.OriginalUrl,
                        document.Url.NormalizedUrl,
                        document.Url.Domain),
                document.Image is null
                    ? null
                    : new SyncImageContent(
                        document.Image.ContentSha256,
                        document.Image.ByteSize,
                        document.Image.PixelWidth,
                        document.Image.PixelHeight,
                        document.Image.MimeType,
                        document.Image.FileExtension,
                        document.Image.OriginalFileName));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "항목 동기화 필드가 올바르지 않습니다.",
                exception);
        }
    }

    private static SyncItemPayloadDocument ToDocument(
        SyncItemPayload payload) =>
        new(
            payload.PayloadVersion,
            payload.ContentKind,
            payload.SourceApp,
            payload.CaptureMethod,
            payload.DeliveryStatus,
            payload.ContextHash,
            payload.CapturedAt,
            payload.ConfirmationSignals,
            payload.Url is null
                ? null
                : new SyncUrlContentDocument(
                    payload.Url.OriginalUrl,
                    payload.Url.NormalizedUrl,
                    payload.Url.Domain),
            payload.Image is null
                ? null
                : new SyncImageContentDocument(
                    payload.Image.ContentSha256,
                    payload.Image.ByteSize,
                    payload.Image.PixelWidth,
                    payload.Image.PixelHeight,
                    payload.Image.MimeType,
                    payload.Image.FileExtension,
                    payload.Image.OriginalFileName));

    private sealed record SyncItemPayloadDocument(
        int PayloadVersion,
        string ContentKind,
        string SourceApp,
        string CaptureMethod,
        string DeliveryStatus,
        string ContextHash,
        DateTimeOffset CapturedAt,
        IReadOnlyList<string> ConfirmationSignals,
        SyncUrlContentDocument? Url,
        SyncImageContentDocument? Image);

    private sealed record SyncUrlContentDocument(
        string OriginalUrl,
        string NormalizedUrl,
        string Domain);

    private sealed record SyncImageContentDocument(
        string ContentSha256,
        long ByteSize,
        int PixelWidth,
        int PixelHeight,
        string MimeType,
        string FileExtension,
        string? OriginalFileName);
}
