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
}
