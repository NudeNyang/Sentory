using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class TelegramRecentSendSignalsTests
{
    [Fact]
    public void ReplaysSendThatArrivedAfterPasteTrigger()
    {
        var signals = new TelegramRecentSendSignals();
        var pastedAt = DateTimeOffset.UtcNow;
        signals.Observe("chat", pastedAt.AddMilliseconds(10));

        Assert.True(signals.CanApply(
            "chat",
            pastedAt,
            pastedAt.AddMilliseconds(20)));
    }

    [Fact]
    public void DoesNotReplayOlderSend()
    {
        var signals = new TelegramRecentSendSignals();
        var pastedAt = DateTimeOffset.UtcNow;
        signals.Observe("chat", pastedAt.AddMilliseconds(-10));

        Assert.False(signals.CanApply("chat", pastedAt, pastedAt));
    }

    [Fact]
    public void ReplaysFastSendAfterSlowNativeDropBaselineCapture()
    {
        var signals = new TelegramRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        signals.Observe("chat", droppedAt.AddMilliseconds(80));

        Assert.True(signals.CanApply(
            "chat",
            droppedAt,
            droppedAt.AddMilliseconds(1500)));
    }

    [Fact]
    public void ReplaysSameProcessSendWhenPhotoDialogHasNoOwner()
    {
        var signals = new TelegramRecentSendSignals();
        var droppedAt = DateTimeOffset.UtcNow;
        signals.ObserveProcess(42, droppedAt.AddMilliseconds(80));

        Assert.True(signals.CanApply(
            "main-window",
            42,
            droppedAt,
            droppedAt.AddMilliseconds(500)));
        Assert.False(signals.CanApply(
            "main-window",
            99,
            droppedAt,
            droppedAt.AddMilliseconds(500)));
    }

    [Fact]
    public void TakingSignalConsumesContextAndProcessAliases()
    {
        var signals = new TelegramRecentSendSignals();
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
        var signals = new TelegramRecentSendSignals();
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
