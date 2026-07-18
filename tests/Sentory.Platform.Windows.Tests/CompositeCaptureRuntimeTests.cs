using Sentory.Core;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class CompositeCaptureRuntimeTests
{
    [Fact]
    public void StartsPausesAndForwardsAllChildRuntimes()
    {
        var first = new FakeRuntime();
        var second = new FakeRuntime();
        var composite = new CompositeCaptureRuntime(first, second);
        CaptureNotification? received = null;
        composite.Captured += (_, notification) => received = notification;

        composite.IsPaused = true;
        composite.Start();
        first.RaiseCaptured();

        Assert.True(first.IsPaused);
        Assert.True(second.IsPaused);
        Assert.True(first.Started);
        Assert.True(second.Started);
        Assert.NotNull(received);
    }

    [Fact]
    public async Task DisposesEveryChildRuntime()
    {
        var first = new FakeRuntime();
        var second = new FakeRuntime();
        var composite = new CompositeCaptureRuntime(first, second);

        await composite.DisposeAsync();

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    private sealed class FakeRuntime : ICaptureRuntime
    {
        public event EventHandler<CaptureNotification>? Captured;

        public event EventHandler<CaptureRuntimeIssue>? IssueDetected;

        public bool IsPaused { get; set; }

        public bool Started { get; private set; }

        public bool Disposed { get; private set; }

        public void Start() => Started = true;

        public void RaiseCaptured() =>
            Captured?.Invoke(
                this,
                new CaptureNotification(
                    ContentKind.Url,
                    1,
                    DateTimeOffset.UtcNow));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void RaiseIssue() =>
            IssueDetected?.Invoke(
                this,
                new CaptureRuntimeIssue(
                    "test",
                    "test",
                    DateTimeOffset.UtcNow));
    }
}
