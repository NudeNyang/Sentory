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
}
