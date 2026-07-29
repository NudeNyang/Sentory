namespace Sentory.Platform.Windows.Runtime;

internal sealed class LineIdleBaselineRefreshScheduler(
    TimeSpan interval,
    Func<Task> refresh,
    Action<Exception>? failed = null)
{
    private DateTimeOffset _nextRefreshAt;

    internal Task ActiveTask { get; private set; } = Task.CompletedTask;

    public bool TryStart(DateTimeOffset now)
    {
        if (!ActiveTask.IsCompleted || now < _nextRefreshAt)
        {
            return false;
        }

        _nextRefreshAt = now + interval;
        ActiveTask = Task.Run(RunRefreshAsync);
        return true;
    }

    private async Task RunRefreshAsync()
    {
        try
        {
            await refresh();
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
        }
    }
}
