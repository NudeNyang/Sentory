using System.Text;
using Sentory.Core.Sync;

namespace Sentory.Core.Tests;

public sealed class SyncMetadataPayloadTests
{
    [Fact]
    public void ItemMetadataRoundTripsCopyFavoriteAndUsageSessions()
    {
        var changedAt = DateTimeOffset.Parse(
            "2026-07-27T12:00:00+09:00");
        var payload = new SyncItemMetadataPayload(
            SyncItemMetadataPayload.CurrentFormatVersion,
            "https://example.com/item",
            changedAt.AddDays(-1),
            4,
            changedAt,
            true,
            changedAt,
            [new SyncUsageSession(changedAt.AddHours(-7), changedAt)]);

        var content = SyncMetadataPayloadSerializer.Serialize(payload);
        var restored = SyncMetadataPayloadSerializer.DeserializeItem(content);

        Assert.Equal(payload.FormatVersion, restored.FormatVersion);
        Assert.Equal(payload.NormalizedKey, restored.NormalizedKey);
        Assert.Equal(payload.ItemCapturedAt, restored.ItemCapturedAt);
        Assert.Equal(payload.DeviceCopyCount, restored.DeviceCopyCount);
        Assert.Equal(payload.LastCopiedAt, restored.LastCopiedAt);
        Assert.Equal(payload.IsFavorite, restored.IsFavorite);
        Assert.Equal(payload.FavoriteChangedAt, restored.FavoriteChangedAt);
        Assert.Equal(payload.UsageSessions, restored.UsageSessions);
        Assert.Contains(
            "\"device_copy_count\":4",
            Encoding.UTF8.GetString(content));
    }

    [Fact]
    public void FavoriteValueRequiresChangeTimestamp()
    {
        var payload = new SyncItemMetadataPayload(
            1,
            "https://example.com/item",
            DateTimeOffset.UtcNow,
            0,
            null,
            true,
            null,
            []);

        Assert.Throws<InvalidDataException>(() =>
            SyncMetadataPayloadSerializer.Serialize(payload));
    }

    [Fact]
    public void AutoFavoriteSettingsRejectUnsupportedThreshold()
    {
        var payload = new SyncAutoFavoriteSettingsPayload(
            1,
            true,
            6,
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidDataException>(() =>
            SyncMetadataPayloadSerializer.Serialize(payload));
    }
}
