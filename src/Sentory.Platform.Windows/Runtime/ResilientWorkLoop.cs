namespace Sentory.Platform.Windows.Runtime;

internal static class ResilientWorkLoop
{
    public static async Task RunAsync<T>(
        IAsyncEnumerable<T> workItems,
        Func<T, CancellationToken, Task> processAsync,
        Action<Exception> reportIssue,
        CancellationToken cancellationToken)
    {
        await foreach (var workItem in workItems.WithCancellation(
                           cancellationToken))
        {
            try
            {
                await processAsync(workItem, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                reportIssue(exception);
            }
        }
    }
}
