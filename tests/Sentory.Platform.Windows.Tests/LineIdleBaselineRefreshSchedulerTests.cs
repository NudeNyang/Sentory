using System.Diagnostics;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class LineIdleBaselineRefreshSchedulerTests
{
    [Fact]
    public async Task TryStart_DoesNotRunSlowRefreshOnCallingThread()
    {
        using var refreshStarted = new ManualResetEventSlim();
        using var releaseRefresh = new ManualResetEventSlim();
        var scheduler = new LineIdleBaselineRefreshScheduler(
            TimeSpan.FromSeconds(2),
            () =>
            {
                refreshStarted.Set();
                releaseRefresh.Wait(TimeSpan.FromSeconds(2));
                return Task.CompletedTask;
            });

        var stopwatch = Stopwatch.StartNew();
        var started = scheduler.TryStart(DateTimeOffset.UtcNow);
        stopwatch.Stop();

        Assert.True(started);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(100),
            $"Refresh blocked the caller for {stopwatch.Elapsed.TotalMilliseconds:F1}ms.");
        Assert.True(refreshStarted.Wait(TimeSpan.FromSeconds(2)));

        releaseRefresh.Set();
        await scheduler.ActiveTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TryStart_PreventsConcurrentAndEarlyRefreshes()
    {
        var calls = 0;
        var now = DateTimeOffset.UtcNow;
        var scheduler = new LineIdleBaselineRefreshScheduler(
            TimeSpan.FromSeconds(2),
            () =>
            {
                Interlocked.Increment(ref calls);
                return Task.CompletedTask;
            });

        Assert.True(scheduler.TryStart(now));
        await scheduler.ActiveTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(scheduler.TryStart(now.AddSeconds(1)));
        Assert.True(scheduler.TryStart(now.AddSeconds(2)));
        await scheduler.ActiveTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, Volatile.Read(ref calls));
    }
}
