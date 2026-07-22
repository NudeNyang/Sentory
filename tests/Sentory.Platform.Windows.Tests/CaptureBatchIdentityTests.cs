using Sentory.Core;
using Sentory.Platform.Windows.Interop;
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

    [Fact]
    public void KeepsOnlyUrlsObservedInSentDiscordMessage()
    {
        var oldUrl = new NormalizedUrl(
            "https://example.com/old",
            "https://example.com/old",
            "example.com");
        var newUrl = new NormalizedUrl(
            "https://example.com/new",
            "https://example.com/new",
            "example.com");

        var confirmed = DiscordConfirmedUrlPolicy.SelectForCapture(
            [oldUrl, newUrl],
            [newUrl.Value]);

        Assert.Equal([newUrl.Value], confirmed.Select(url => url.Value));
    }

    [Fact]
    public void CombinesSeparatelyPastedImagesConfirmedByOneDiscordSend()
    {
        var leaderEventId = Guid.Parse(
            "2670be52-a1c6-49ba-9076-03ff706b3a66");
        var sentAt = DateTimeOffset.Parse("2026-07-20T12:00:00Z");
        var first = CreateImage("HASH-A");
        var second = CreateImage("HASH-B");
        var repeatedFirst = CreateImage("hash-a");

        var batch = new DiscordImageSendBatch(
            leaderEventId,
            "discord-context",
            sentAt,
            [],
            [first]);
        batch.Add([], [second, repeatedFirst]);

        Assert.True(batch.IsLeader(leaderEventId));
        Assert.Equal(
            [first.Sha256, second.Sha256],
            batch.SnapshotImages().Select(image => image.Sha256));
    }

    [Fact]
    public void CancelsOldImageCandidateRemovedBeforeNextPaste()
    {
        var now = DateTimeOffset.Parse("2026-07-22T03:00:10Z");
        var first = new DiscordPendingImageCandidate(
            Guid.Parse("46c52d12-7745-445e-adb2-02927e41beb0"),
            now.AddSeconds(-5),
            1);
        var second = new DiscordPendingImageCandidate(
            Guid.Parse("5b10b0dd-41a9-45b7-9e4f-fbf989759d99"),
            now.AddMilliseconds(-100),
            1);

        var cancelled = DiscordDraftImageCandidatePolicy
            .SelectCandidatesToCancel([first, second], 1, now);

        Assert.Equal([first.EventId], cancelled);
    }

    [Fact]
    public void KeepsAllCandidatesWhenDiscordStillShowsEveryImage()
    {
        var now = DateTimeOffset.Parse("2026-07-22T03:00:10Z");
        var candidates = new[]
        {
            new DiscordPendingImageCandidate(Guid.NewGuid(), now.AddSeconds(-5), 1),
            new DiscordPendingImageCandidate(Guid.NewGuid(), now.AddSeconds(-4), 1)
        };

        var cancelled = DiscordDraftImageCandidatePolicy
            .SelectCandidatesToCancel(candidates, 2, now);

        Assert.Empty(cancelled);
    }

    [Fact]
    public void DoesNotCancelNewCandidateBeforePreviewAppears()
    {
        var now = DateTimeOffset.Parse("2026-07-22T03:00:10Z");
        var candidate = new DiscordPendingImageCandidate(
            Guid.NewGuid(),
            now.AddMilliseconds(-100),
            1);

        var cancelled = DiscordDraftImageCandidatePolicy
            .SelectCandidatesToCancel([candidate], 0, now);

        Assert.Empty(cancelled);
    }

    [Fact]
    public void DoesNotDropWholeMultiImagePasteForPartialDifference()
    {
        var now = DateTimeOffset.Parse("2026-07-22T03:00:10Z");
        var candidate = new DiscordPendingImageCandidate(
            Guid.NewGuid(),
            now.AddSeconds(-5),
            2);

        var cancelled = DiscordDraftImageCandidatePolicy
            .SelectCandidatesToCancel([candidate], 1, now);

        Assert.Empty(cancelled);
    }

    private static ClipboardImageSnapshot CreateImage(string hash) =>
        new([1, 2, 3], hash, 1, 1, "image/png", ".png");
}
