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
}
