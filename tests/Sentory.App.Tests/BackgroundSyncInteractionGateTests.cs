using System.Diagnostics;

namespace Sentory.App.Tests;

public sealed class BackgroundSyncInteractionGateTests
{
    [Fact]
    public async Task ForegroundInteractionCancelsActiveBackgroundWork()
    {
        var gate = new BackgroundSyncInteractionGate(
            TimeSpan.FromMilliseconds(10));
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var work = gate.RunWhenQuietAsync(
            async cancellationToken =>
            {
                using var registration = cancellationToken.Register(
                    () => Thread.Sleep(250));
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var notificationTimer = Stopwatch.StartNew();
        gate.NotifyForegroundInteraction();

        Assert.True(
            notificationTimer.Elapsed < TimeSpan.FromMilliseconds(100));
        Assert.False(await work.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ForegroundInteractionDefersNewBackgroundWork()
    {
        var gate = new BackgroundSyncInteractionGate(
            TimeSpan.FromMilliseconds(80));
        gate.NotifyForegroundInteraction();
        var stopwatch = Stopwatch.StartNew();

        var completed = await gate.RunWhenQuietAsync(
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.True(completed);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(50));
    }
}
