using System.Diagnostics;

namespace Sentory.App;

internal sealed class BackgroundSyncInteractionGate
{
    private readonly object _gate = new();
    private readonly TimeSpan _quietPeriod;
    private long _lastInteractionTimestamp;
    private CancellationTokenSource? _activeWork;

    public BackgroundSyncInteractionGate(TimeSpan quietPeriod)
    {
        if (quietPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(quietPeriod));
        }

        _quietPeriod = quietPeriod;
    }

    public void NotifyForegroundInteraction()
    {
        CancellationTokenSource? activeWork;
        lock (_gate)
        {
            _lastInteractionTimestamp = Stopwatch.GetTimestamp();
            activeWork = _activeWork;
        }

        if (activeWork is not null)
        {
            _ = CancelActiveWorkAsync(activeWork);
        }
    }

    public async Task<bool> RunWhenQuietAsync(
        Func<CancellationToken, Task> work,
        CancellationToken shutdownToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        while (true)
        {
            await WaitUntilQuietAsync(shutdownToken);
            var workCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(shutdownToken);
            lock (_gate)
            {
                var quietFor = GetQuietDuration();
                if (quietFor < _quietPeriod)
                {
                    workCancellation.Dispose();
                    continue;
                }

                _activeWork = workCancellation;
            }

            try
            {
                await work(workCancellation.Token);
                return true;
            }
            catch (OperationCanceledException)
                when (workCancellation.IsCancellationRequested &&
                      !shutdownToken.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_activeWork, workCancellation))
                    {
                        _activeWork = null;
                    }
                }

                workCancellation.Dispose();
            }
        }
    }

    private async Task WaitUntilQuietAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan remaining;
            lock (_gate)
            {
                remaining = _quietPeriod - GetQuietDuration();
            }

            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(remaining, cancellationToken);
        }
    }

    private TimeSpan GetQuietDuration() =>
        _lastInteractionTimestamp == 0
            ? TimeSpan.MaxValue
            : Stopwatch.GetElapsedTime(_lastInteractionTimestamp);

    private static async Task CancelActiveWorkAsync(
        CancellationTokenSource activeWork)
    {
        try
        {
            await activeWork.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
