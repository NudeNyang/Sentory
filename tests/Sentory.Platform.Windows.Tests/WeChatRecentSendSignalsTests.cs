using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class WeChatRecentSendSignalsTests
{
    [Fact]
    public void ReplaysFastUrlSendWithoutComposerTreeRead()
    {
        var signals = new WeChatRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-30T10:00:00Z");
        var sentAt = pastedAt.AddMilliseconds(25);
        signals.Observe("context", sentAt);

        Assert.True(signals.CanApply(
            "context",
            42,
            pastedAt,
            sentAt.AddMilliseconds(50)));
    }

    [Fact]
    public void DoesNotReplaySendObservedBeforePaste()
    {
        var signals = new WeChatRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var sentAt = pastedAt.AddMilliseconds(-25);
        signals.Observe("context", sentAt);

        Assert.False(signals.CanApply(
            "context",
            42,
            pastedAt,
            pastedAt.AddMilliseconds(50)));
    }

    [Fact]
    public void ReplaysImageSendWithoutComposerText()
    {
        var signals = new WeChatRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var sentAt = pastedAt.AddMilliseconds(25);
        signals.ObserveProcess(42, sentAt);

        Assert.True(signals.CanApply(
            "missing-context",
            42,
            pastedAt,
            sentAt.AddMilliseconds(50)));
    }

    [Fact]
    public void TakingSignalConsumesContextAndProcessAliases()
    {
        var signals = new WeChatRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        var sentAt = droppedAt.AddMilliseconds(80);
        signals.Observe("main-window", sentAt);
        signals.ObserveProcess(42, sentAt);

        Assert.True(signals.TryTakeApplicable(
            "main-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(500)));
        Assert.False(signals.TryTakeApplicable(
            "other-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(501)));
    }

    [Fact]
    public void ImmediateConsumptionPreventsRegistrationReplay()
    {
        var signals = new WeChatRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        var sentAt = droppedAt.AddMilliseconds(80);
        signals.Observe("main-window", sentAt);
        signals.ObserveProcess(42, sentAt);

        Assert.True(signals.TryConsume(sentAt));
        Assert.False(signals.TryTakeApplicable(
            "main-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(500)));
    }
}
