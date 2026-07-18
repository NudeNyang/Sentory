using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class ResilientWorkLoopTests
{
    [Fact]
    public async Task ContinuesWithNextItemAfterProcessingFailure()
    {
        var processed = new List<int>();
        var issues = new List<Exception>();

        await ResilientWorkLoop.RunAsync(
            GetItems(),
            (item, _) =>
            {
                if (item == 2)
                {
                    throw new InvalidOperationException("test failure");
                }

                processed.Add(item);
                return Task.CompletedTask;
            },
            issues.Add,
            CancellationToken.None);

        Assert.Equal([1, 3], processed);
        Assert.Single(issues);
        Assert.IsType<InvalidOperationException>(issues[0]);
    }

    [Fact]
    public async Task PropagatesRequestedCancellationWithoutReportingIssue()
    {
        using var cancellation = new CancellationTokenSource();
        var issues = new List<Exception>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ResilientWorkLoop.RunAsync(
                GetItems(),
                (_, token) =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                issues.Add,
                cancellation.Token));

        Assert.Empty(issues);
    }

    private static async IAsyncEnumerable<int> GetItems()
    {
        yield return 1;
        await Task.Yield();
        yield return 2;
        yield return 3;
    }
}
