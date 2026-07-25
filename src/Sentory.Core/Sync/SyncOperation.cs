using System.Security.Cryptography;

namespace Sentory.Core.Sync;

public enum SyncOperationKind
{
    Upsert,
    Delete,
    Restore
}

public sealed record SyncOperation
{
    public const int CurrentFormatVersion = 1;
    public const int MaximumPayloadBytes = 1024 * 1024;
    public const string NoEncryption = "none";
    private readonly byte[] _payload;

    public SyncOperation(
        int formatVersion,
        string encryptionMode,
        Guid operationId,
        string deviceId,
        long sequence,
        Guid itemId,
        SyncOperationKind kind,
        DateTimeOffset occurredAt,
        string payloadSha256,
        byte[] payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptionMode);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadSha256);
        ArgumentNullException.ThrowIfNull(payload);

        if (formatVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(formatVersion));
        }

        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "동기화 작업 ID가 필요합니다.",
                nameof(operationId));
        }

        if (!SyncDeviceIdentity.IsValid(deviceId))
        {
            throw new ArgumentException(
                "동기화 기기 ID 형식이 올바르지 않습니다.",
                nameof(deviceId));
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException(
                "동기화 항목 ID가 필요합니다.",
                nameof(itemId));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (payload.Length > MaximumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"동기화 작업 본문은 {MaximumPayloadBytes}바이트 이하여야 합니다.");
        }

        if (!IsSha256(payloadSha256))
        {
            throw new ArgumentException(
                "동기화 본문 SHA-256 형식이 올바르지 않습니다.",
                nameof(payloadSha256));
        }

        FormatVersion = formatVersion;
        EncryptionMode = encryptionMode;
        OperationId = operationId;
        DeviceId = deviceId;
        Sequence = sequence;
        ItemId = itemId;
        Kind = kind;
        OccurredAt = occurredAt;
        PayloadSha256 = payloadSha256.ToLowerInvariant();
        _payload = payload.ToArray();
    }

    public int FormatVersion { get; }

    public string EncryptionMode { get; }

    public Guid OperationId { get; }

    public string DeviceId { get; }

    public long Sequence { get; }

    public Guid ItemId { get; }

    public SyncOperationKind Kind { get; }

    public DateTimeOffset OccurredAt { get; }

    public string PayloadSha256 { get; }

    public byte[] Payload => _payload.ToArray();

    public static SyncOperation Create(
        string deviceId,
        long sequence,
        Guid itemId,
        SyncOperationKind kind,
        DateTimeOffset occurredAt,
        ReadOnlySpan<byte> payload,
        Guid? operationId = null)
    {
        var payloadBytes = payload.ToArray();
        return new SyncOperation(
            CurrentFormatVersion,
            NoEncryption,
            operationId ?? Guid.NewGuid(),
            deviceId,
            sequence,
            itemId,
            kind,
            occurredAt,
            Convert.ToHexString(SHA256.HashData(payloadBytes))
                .ToLowerInvariant(),
            payloadBytes);
    }

    public bool HasValidPayloadHash()
    {
        var actual = SHA256.HashData(_payload);
        var expected = Convert.FromHexString(PayloadSha256);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 &&
        value.All(Uri.IsHexDigit);
}

public static class SyncDeviceIdentity
{
    public static string Create() => Guid.NewGuid().ToString("N");

    public static bool IsValid(string? value) =>
        value is { Length: 32 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
