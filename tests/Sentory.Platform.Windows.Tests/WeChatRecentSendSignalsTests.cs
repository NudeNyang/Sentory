using Sentory.Core;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class WeChatRecentSendSignalsTests
{
    [Fact]
    public void ReplaysFastSendWhenComposerStillContainsCandidateUrl()
    {
        var signals = new WeChatRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var sentAt = pastedAt.AddMilliseconds(25);
        var urls = UrlExtractor.Extract("https://example.com/path");
        signals.Observe("context", sentAt, "https://example.com/path");

        Assert.True(signals.CanApply(
            "context",
            42,
            pastedAt,
            sentAt.AddMilliseconds(50),
            urls,
            hasImages: false));
    }

    [Fact]
    public void DoesNotReplayRemovedUrlForLaterSend()
    {
        var signals = new WeChatRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var sentAt = pastedAt.AddMilliseconds(25);
        var urls = UrlExtractor.Extract("https://example.com/path");
        signals.Observe("context", sentAt, "다른 메시지");

        Assert.False(signals.CanApply(
            "context",
            42,
            pastedAt,
            sentAt.AddMilliseconds(50),
            urls,
            hasImages: false));
    }

    [Fact]
    public void ReplaysImageSendWithoutComposerText()
    {
        var signals = new WeChatRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var sentAt = pastedAt.AddMilliseconds(25);
        signals.ObserveProcess(42, sentAt, composerText: null);

        Assert.True(signals.CanApply(
            "missing-context",
            42,
            pastedAt,
            sentAt.AddMilliseconds(50),
            [],
            hasImages: true));
    }

    [Fact]
    public void TakingSignalConsumesContextAndProcessAliases()
    {
        var signals = new WeChatRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        var sentAt = droppedAt.AddMilliseconds(80);
        signals.Observe("main-window", sentAt, composerText: null);
        signals.ObserveProcess(42, sentAt, composerText: null);

        Assert.True(signals.TryTakeApplicable(
            "main-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(500),
            [],
            hasImages: true));
        Assert.False(signals.TryTakeApplicable(
            "other-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(501),
            [],
            hasImages: true));
    }

    [Fact]
    public void ImmediateConsumptionPreventsRegistrationReplay()
    {
        var signals = new WeChatRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        var sentAt = droppedAt.AddMilliseconds(80);
        signals.Observe("main-window", sentAt, composerText: null);
        signals.ObserveProcess(42, sentAt, composerText: null);

        Assert.True(signals.TryConsume(sentAt));
        Assert.False(signals.TryTakeApplicable(
            "main-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(500),
            [],
            hasImages: true));
    }
}
