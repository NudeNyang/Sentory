using Sentory.Core;
using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class MessengerCandidateDuplicatePolicyTests
{
    [Fact]
    public void RejectsOnlySamePayloadBurstInSameConversation()
    {
        var occurredAt = DateTimeOffset.Parse("2026-07-29T03:00:00Z");
        var signature = CreateSignature("A", "B");

        Assert.True(MessengerCandidateDuplicatePolicy.IsDuplicateBurst(
            "chat",
            occurredAt,
            signature,
            "chat",
            occurredAt.AddMilliseconds(400),
            signature));
        Assert.False(MessengerCandidateDuplicatePolicy.IsDuplicateBurst(
            "chat",
            occurredAt,
            signature,
            "chat",
            occurredAt.AddMilliseconds(501),
            signature));
        Assert.False(MessengerCandidateDuplicatePolicy.IsDuplicateBurst(
            "chat",
            occurredAt,
            signature,
            "other-chat",
            occurredAt.AddMilliseconds(100),
            signature));
    }

    [Fact]
    public void OverlappingCollectionsRemainDistinctCandidates()
    {
        var first = CreateSignature("A", "B");
        var second = CreateSignature("B", "C");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void PayloadOrderDoesNotChangeSignature()
    {
        Assert.Equal(
            CreateSignature("A", "B"),
            CreateSignature("B", "A"));
    }

    private static string CreateSignature(params string[] hashes) =>
        MessengerCandidateDuplicatePolicy.CreatePayloadSignature(
            [],
            hashes.Select(hash => new ClipboardImageSnapshot(
                [],
                hash,
                1,
                1,
                "image/png",
                ".png")).ToList());
}
