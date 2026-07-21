namespace Sentory.Platform.Windows.Runtime;

internal static class BackgroundCancellation
{
    public static Task Request(
        IReadOnlyList<CancellationTokenSource> cancellations) =>
        Task.Run(() =>
        {
            foreach (var cancellation in cancellations)
            {
                try
                {
                    cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // A candidate can complete after the pause snapshot.
                }
            }
        });
}
