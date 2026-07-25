using System.Text;
using Sentory.Core.Sync;

namespace Sentory.Core.Tests;

public sealed class SyncItemPayloadTests
{
    [Fact]
    public void UrlPayloadRoundTripsWithStringProtocolValues()
    {
        var payload = SyncItemPayload.CreateUrl(
            new SyncUrlContent(
                "https://example.com/path?b=2&a=1",
                "https://example.com/path?a=1&b=2",
                "example.com"),
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            "context",
            DateTimeOffset.Parse("2026-07-26T08:30:00+09:00"),
            ["url-match"]);

        var content = SyncItemPayloadSerializer.Serialize(payload);
        var restored = SyncItemPayloadSerializer.Deserialize(content);
        var json = Encoding.UTF8.GetString(content);

        Assert.Contains("\"content_kind\":\"url\"", json);
        Assert.Contains("\"source_app\":\"Discord\"", json);
        Assert.Equal(payload.PayloadVersion, restored.PayloadVersion);
        Assert.Equal(payload.ContentKind, restored.ContentKind);
        Assert.Equal(payload.SourceApp, restored.SourceApp);
        Assert.Equal(payload.Url, restored.Url);
        Assert.Equal(payload.ConfirmationSignals, restored.ConfirmationSignals);
    }

    [Fact]
    public void ImagePayloadRoundTripsWithBlobMetadata()
    {
        var sha256 = new string('a', 64);
        var payload = SyncItemPayload.CreateImage(
            new SyncImageContent(
                sha256,
                1234,
                1920,
                1080,
                "image/png",
                ".png",
                "capture.png"),
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVImage,
            DeliveryStatus.NotObserved,
            "context",
            DateTimeOffset.UtcNow,
            ["clipboard"]);

        var restored = SyncItemPayloadSerializer.Deserialize(
            SyncItemPayloadSerializer.Serialize(payload));

        Assert.Equal(SyncItemContentKinds.Image, restored.ContentKind);
        Assert.Null(restored.Url);
        Assert.Equal(payload.Image, restored.Image);
    }

    [Fact]
    public void NewerPayloadVersionIsRejected()
    {
        var payload = SyncItemPayload.CreateUrl(
            new SyncUrlContent(
                "https://example.com/",
                "https://example.com/",
                "example.com"),
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            "context",
            DateTimeOffset.UtcNow,
            []);
        var json = Encoding.UTF8.GetString(
            SyncItemPayloadSerializer.Serialize(payload))
            .Replace(
                "\"payload_version\":1",
                "\"payload_version\":2",
                StringComparison.Ordinal);

        Assert.Throws<NotSupportedException>(() =>
            SyncItemPayloadSerializer.Deserialize(
                Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void ConfirmationSignalsCannotMutatePayload()
    {
        var signals = new[] { "original" };
        var payload = SyncItemPayload.CreateUrl(
            new SyncUrlContent(
                "https://example.com/",
                "https://example.com/",
                "example.com"),
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            "context",
            DateTimeOffset.UtcNow,
            signals);

        signals[0] = "changed";
        var returned = Assert.IsType<string[]>(
            payload.ConfirmationSignals);
        returned[0] = "also-changed";

        Assert.Equal(
            "original",
            Assert.Single(payload.ConfirmationSignals));
    }

    [Fact]
    public void BlobKeyRoundTripsCanonicalSha256()
    {
        var sha256 = string.Concat(
            "ab",
            new string('c', 62));

        var key = SyncBlobObjectKey.Create(sha256.ToUpperInvariant());

        Assert.Equal($"blobs/sha256/ab/{sha256}", key);
        Assert.True(SyncBlobObjectKey.TryParse(key, out var restored));
        Assert.Equal(sha256, restored);
        Assert.False(SyncBlobObjectKey.TryParse(
            $"blobs/sha256/ff/{sha256}",
            out _));
    }
}
