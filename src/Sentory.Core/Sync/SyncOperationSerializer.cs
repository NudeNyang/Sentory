using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sentory.Core.Sync;

public static class SyncOperationSerializer
{
    public const int MaximumSerializedBytes =
        (((SyncOperation.MaximumPayloadBytes + 2) / 3) * 4) +
        (64 * 1024);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    static SyncOperationSerializer()
    {
        JsonOptions.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.SnakeCaseLower));
    }

    public static byte[] Serialize(SyncOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnsureSupported(operation);
        if (!operation.HasValidPayloadHash())
        {
            throw new InvalidDataException(
                "동기화 작업 본문의 SHA-256이 일치하지 않습니다.");
        }

        return JsonSerializer.SerializeToUtf8Bytes(
            new SyncOperationDocument(
                operation.FormatVersion,
                operation.EncryptionMode,
                operation.OperationId,
                operation.DeviceId,
                operation.Sequence,
                operation.ItemId,
                operation.Kind,
                operation.OccurredAt,
                operation.PayloadSha256,
                operation.Payload),
            JsonOptions);
    }

    public static SyncOperation Deserialize(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0 || content.Length > MaximumSerializedBytes)
        {
            throw new InvalidDataException(
                "동기화 작업 파일 크기가 허용 범위를 벗어났습니다.");
        }

        SyncOperationDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SyncOperationDocument>(
                content,
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "동기화 작업 JSON을 읽을 수 없습니다.",
                exception);
        }

        if (document is null)
        {
            throw new InvalidDataException(
                "동기화 작업 JSON이 비어 있습니다.");
        }

        SyncOperation operation;
        try
        {
            operation = new SyncOperation(
                document.FormatVersion,
                document.EncryptionMode,
                document.OperationId,
                document.DeviceId,
                document.Sequence,
                document.ItemId,
                document.Kind,
                document.OccurredAt,
                document.PayloadSha256,
                document.Payload);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "동기화 작업 필드가 올바르지 않습니다.",
                exception);
        }

        EnsureSupported(operation);
        if (!operation.HasValidPayloadHash())
        {
            throw new InvalidDataException(
                "동기화 작업 본문의 SHA-256이 일치하지 않습니다.");
        }

        return operation;
    }

    private static void EnsureSupported(SyncOperation operation)
    {
        if (operation.FormatVersion > SyncOperation.CurrentFormatVersion)
        {
            throw new NotSupportedException(
                "현재 Sentory보다 새로운 동기화 작업 형식입니다.");
        }

        if (!string.Equals(
                operation.EncryptionMode,
                SyncOperation.NoEncryption,
                StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "현재 Sentory가 지원하지 않는 동기화 암호화 형식입니다.");
        }
    }

    private sealed record SyncOperationDocument(
        int FormatVersion,
        string EncryptionMode,
        Guid OperationId,
        string DeviceId,
        long Sequence,
        Guid ItemId,
        SyncOperationKind Kind,
        DateTimeOffset OccurredAt,
        string PayloadSha256,
        byte[] Payload);
}
