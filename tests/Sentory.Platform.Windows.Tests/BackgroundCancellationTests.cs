using System.Diagnostics;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class BackgroundCancellationTests
{
    [Fact]
    public async Task RequestReturnsBeforeCancellationCallbackCompletes()
    {
        using var cancellation = new CancellationTokenSource();
        using var callbackStarted = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        using var registration = cancellation.Token.Register(() =>
        {
            callbackStarted.Set();
            releaseCallback.Wait(TimeSpan.FromSeconds(5));
        });

        var timer = Stopwatch.StartNew();
        var work = BackgroundCancellation.Request([cancellation]);
        timer.Stop();

        try
        {
            Assert.True(timer.Elapsed < TimeSpan.FromMilliseconds(250));
            Assert.True(callbackStarted.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(work.IsCompleted);
        }
        finally
        {
            releaseCallback.Set();
        }

        await work.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
