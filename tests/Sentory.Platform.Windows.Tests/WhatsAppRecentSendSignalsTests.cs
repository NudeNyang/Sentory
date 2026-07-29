using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class WhatsAppRecentSendSignalsTests
{
    [Fact]
    public void AppliesEnterObservedBeforeCandidateRegistration()
    {
        var signals = new WhatsAppRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T03:00:00Z");
        var sentAt = pastedAt.AddMilliseconds(40);

        signals.Observe("same-window", sentAt);

        Assert.True(signals.CanApply(
            "same-window",
            pastedAt,
            sentAt.AddMilliseconds(300)));
    }

    [Fact]
    public void RejectsSendFromAnotherWindowOrBeforePaste()
    {
        var signals = new WhatsAppRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T03:00:00Z");

        signals.Observe("other-window", pastedAt.AddMilliseconds(40));
        signals.Observe("same-window", pastedAt.AddMilliseconds(-40));

        Assert.False(signals.CanApply(
            "same-window",
            pastedAt,
            pastedAt.AddMilliseconds(300)));
        Assert.False(signals.CanApply(
            "missing-window",
            pastedAt,
            pastedAt.AddMilliseconds(300)));
    }

    [Fact]
    public void RejectsExpiredSendSignal()
    {
        var signals = new WhatsAppRecentSendSignals();
        var pastedAt = DateTimeOffset.Parse("2026-07-28T03:00:00Z");
        var sentAt = pastedAt.AddMilliseconds(40);

        signals.Observe("same-window", sentAt);

        Assert.False(signals.CanApply(
            "same-window",
            pastedAt,
            sentAt.AddMinutes(3)));
    }

    [Fact]
    public void TakingSignalConsumesContextAndProcessAliases()
    {
        var signals = new WhatsAppRecentSendSignals();
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
        var signals = new WhatsAppRecentSendSignals();
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
