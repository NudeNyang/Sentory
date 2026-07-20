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
        CaptureRuntimeStatus? receivedStatus = null;
        composite.Captured += (_, notification) => received = notification;
        composite.StatusChanged += (_, status) => receivedStatus = status;

        composite.IsPaused = true;
        composite.Start();
        first.RaiseCaptured();
        first.RaiseStatus(CaptureRuntimeState.Ready);

        Assert.True(first.IsPaused);
        Assert.True(second.IsPaused);
        Assert.True(first.Started);
        Assert.True(second.Started);
        Assert.NotNull(received);
        Assert.Equal(CaptureRuntimeState.Ready, receivedStatus?.State);
    }

    [Fact]
    public void ForwardsRecoveryRequestOnlyToSupportingRuntime()
    {
        var first = new FakeRuntime();
        var composite = new CompositeCaptureRuntime(first);

        composite.RequestRecovery(SourceApp.Discord);

        Assert.Equal(SourceApp.Discord, first.RecoveryRequestedFor);
    }

    [Fact]
    public void PausesOnlyTheDisabledMessengerAndPreservesGlobalPause()
    {
        var kakao = new FakeRuntime();
        var discord = new FakeRuntime();
        var composite = new CompositeCaptureRuntime(
            (SourceApp.KakaoTalk, kakao),
            (SourceApp.Discord, discord));

        composite.SetSourceEnabled(SourceApp.Discord, false);

        Assert.False(kakao.IsPaused);
        Assert.True(discord.IsPaused);
        Assert.True(composite.IsSourceEnabled(SourceApp.KakaoTalk));
        Assert.False(composite.IsSourceEnabled(SourceApp.Discord));

        composite.IsPaused = true;
        Assert.True(kakao.IsPaused);
        Assert.True(discord.IsPaused);

        composite.IsPaused = false;
        Assert.False(kakao.IsPaused);
        Assert.True(discord.IsPaused);
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

    [Fact]
    public async Task BeginsDisposingAllChildRuntimesWithoutWaitingForEachOther()
    {
        var first = new BlockingRuntime();
        var second = new BlockingRuntime();
        var composite = new CompositeCaptureRuntime(first, second);

        var disposal = composite.DisposeAsync().AsTask();
        try
        {
            await Task.WhenAll(
                    first.DisposeStarted.Task,
                    second.DisposeStarted.Task)
                .WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            first.AllowDispose.TrySetResult();
            second.AllowDispose.TrySetResult();
            await disposal;
        }

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    private sealed class FakeRuntime :
        ICaptureRuntime,
        ICaptureRuntimeStatusSource,
        ICaptureRuntimeRecoveryController
    {
        public event EventHandler<CaptureNotification>? Captured;

        public event EventHandler<CaptureRuntimeIssue>? IssueDetected;

        public event EventHandler<CaptureRuntimeStatus>? StatusChanged;

        public bool IsPaused { get; set; }

        public bool Started { get; private set; }

        public bool Disposed { get; private set; }

        public SourceApp? RecoveryRequestedFor { get; private set; }

        public void Start() => Started = true;

        public void RaiseCaptured() =>
            Captured?.Invoke(
                this,
                new CaptureNotification(
                    ContentKind.Url,
                    1,
                    DateTimeOffset.UtcNow));

        public void RaiseStatus(CaptureRuntimeState state) =>
            StatusChanged?.Invoke(
                this,
                new CaptureRuntimeStatus(
                    SourceApp.Discord,
                    state,
                    DateTimeOffset.UtcNow));

        public void RequestRecovery(SourceApp sourceApp) =>
            RecoveryRequestedFor = sourceApp;

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

    private sealed class BlockingRuntime : ICaptureRuntime
    {
        public event EventHandler<CaptureNotification>? Captured
        {
            add { }
            remove { }
        }

        public event EventHandler<CaptureRuntimeIssue>? IssueDetected
        {
            add { }
            remove { }
        }

        public TaskCompletionSource DisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsPaused { get; set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
        }

        public async ValueTask DisposeAsync()
        {
            DisposeStarted.TrySetResult();
            await AllowDispose.Task;
            Disposed = true;
        }
    }
}
