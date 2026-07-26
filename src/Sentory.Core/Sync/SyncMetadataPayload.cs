using System.Text.Json;

namespace Sentory.Core.Sync;

public sealed record SyncUsageSession(
    DateTimeOffset StartedAt,
    DateTimeOffset LastEventAt);

public sealed record SyncItemMetadataPayload(
    int FormatVersion,
    string NormalizedKey,
    DateTimeOffset ItemCapturedAt,
    long DeviceCopyCount,
    DateTimeOffset? LastCopiedAt,
    bool? IsFavorite,
    DateTimeOffset? FavoriteChangedAt,
    IReadOnlyList<SyncUsageSession> UsageSessions)
{
    public const int CurrentFormatVersion = 1;
}

public sealed record SyncAutoFavoriteSettingsPayload(
    int FormatVersion,
    bool Enabled,
    int UsageThreshold,
    DateTimeOffset ChangedAt)
{
    public const int CurrentFormatVersion = 1;
}

public static class SyncMetadataPayloadSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static byte[] Serialize(SyncItemMetadataPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Validate(payload);
        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
    }

    public static SyncItemMetadataPayload DeserializeItem(
        ReadOnlySpan<byte> content)
    {
        var payload = Deserialize<SyncItemMetadataPayload>(content);
        Validate(payload);
        return payload;
    }

    public static byte[] Serialize(
        SyncAutoFavoriteSettingsPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Validate(payload);
        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
    }

    public static SyncAutoFavoriteSettingsPayload DeserializeSettings(
        ReadOnlySpan<byte> content)
    {
        var payload = Deserialize<SyncAutoFavoriteSettingsPayload>(content);
        Validate(payload);
        return payload;
    }

    private static T Deserialize<T>(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0 ||
            content.Length > SyncOperation.MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                "동기화 메타데이터 크기가 허용 범위를 벗어났습니다.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions) ??
                   throw new InvalidDataException(
                       "동기화 메타데이터가 비어 있습니다.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "동기화 메타데이터 JSON을 읽을 수 없습니다.",
                exception);
        }
    }

    private static void Validate(SyncItemMetadataPayload payload)
    {
        if (payload.FormatVersion is <= 0 or >
            SyncItemMetadataPayload.CurrentFormatVersion)
        {
            throw new NotSupportedException(
                "지원하지 않는 항목 메타데이터 형식입니다.");
        }

        if (string.IsNullOrWhiteSpace(payload.NormalizedKey) ||
            payload.DeviceCopyCount < 0 ||
            payload.UsageSessions is null ||
            payload.UsageSessions.Count > 512 ||
            payload.UsageSessions.Any(session =>
                session.StartedAt > session.LastEventAt) ||
            payload.IsFavorite.HasValue !=
            payload.FavoriteChangedAt.HasValue)
        {
            throw new InvalidDataException(
                "항목 메타데이터 필드가 올바르지 않습니다.");
        }
    }

    private static void Validate(
        SyncAutoFavoriteSettingsPayload payload)
    {
        if (payload.FormatVersion is <= 0 or >
            SyncAutoFavoriteSettingsPayload.CurrentFormatVersion)
        {
            throw new NotSupportedException(
                "지원하지 않는 자동 즐겨찾기 설정 형식입니다.");
        }

        if (payload.UsageThreshold is < 2 or > 5)
        {
            throw new InvalidDataException(
                "자동 즐겨찾기 기준값이 올바르지 않습니다.");
        }
    }
}
