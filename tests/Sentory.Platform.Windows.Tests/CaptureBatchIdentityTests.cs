using Sentory.Core;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class CaptureBatchIdentityTests
{
    [Fact]
    public void CreatesStableDistinctEventIdsForImagesInOnePaste()
    {
        var pasteId = Guid.Parse("2d099633-038c-4983-b8d5-28fc4c58454e");

        var first = CaptureBatchIdentity.ForImage(pasteId, "HASH-A");
        var repeated = CaptureBatchIdentity.ForImage(pasteId, "HASH-A");
        var second = CaptureBatchIdentity.ForImage(pasteId, "HASH-B");

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void KeepsDelayedImageSendSignalButRejectsDelayedUrlSignal()
    {
        var pastedAt = DateTimeOffset.Parse("2026-07-20T12:00:00Z");
        var sentAt = pastedAt.AddSeconds(1);
        var observedAt = sentAt.AddSeconds(12);

        Assert.True(DiscordSendSignalPolicy.CanAssociate(
            pastedAt, sentAt, observedAt, isImage: true));
        Assert.False(DiscordSendSignalPolicy.CanAssociate(
            pastedAt, sentAt, observedAt, isImage: false));
    }

    [Fact]
    public void CombinesSeparatelyPastedUrlsConfirmedByOneDiscordSend()
    {
        var leaderEventId = Guid.Parse(
            "93a62048-d53e-4c3d-87ae-fe8e044bc3ac");
        var sentAt = DateTimeOffset.Parse("2026-07-20T12:00:00Z");
        var first = new NormalizedUrl(
            "https://example.com/first",
            "https://example.com/first",
            "example.com");
        var second = new NormalizedUrl(
            "https://example.com/second",
            "https://example.com/second",
            "example.com");
        var repeatedFirst = first with
        {
            Original = "https://example.com/first#copied-again"
        };

        var batch = new DiscordUrlSendBatch(
            leaderEventId,
            "discord-context",
            sentAt,
            [first]);
        batch.Add([second, repeatedFirst]);

        Assert.True(batch.IsLeader(leaderEventId));
        Assert.Equal(
            [first.Value, second.Value],
            batch.SnapshotUrls().Select(url => url.Value));
    }
}
