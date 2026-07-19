using Sentory.Core;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordDetectionStatusTrackerTests
{
    [Fact]
    public void PublishesEachDistinctStateOnce()
    {
        var tracker = new DiscordDetectionStatusTracker();
        var received = new List<CaptureRuntimeState>();
        tracker.StatusChanged += (_, status) => received.Add(status.State);

        Assert.True(tracker.Publish(CaptureRuntimeState.Connecting));
        Assert.False(tracker.Publish(CaptureRuntimeState.Connecting));
        Assert.True(tracker.Publish(CaptureRuntimeState.Ready));
        Assert.True(tracker.Publish(CaptureRuntimeState.Recovering));
        Assert.True(tracker.Publish(CaptureRuntimeState.Ready));

        Assert.Equal(
            [
                CaptureRuntimeState.Connecting,
                CaptureRuntimeState.Ready,
                CaptureRuntimeState.Recovering,
                CaptureRuntimeState.Ready
            ],
            received);
        Assert.Equal(CaptureRuntimeState.Ready, tracker.Current);
    }

    [Theory]
    [InlineData("worker-process-exited", true)]
    [InlineData("worker-client:IOException", true)]
    [InlineData("discord-message-list-unavailable", false)]
    public void DistinguishesWorkerRecoveryFromDiscordReconnect(
        string signal,
        bool expected)
    {
        var response = DiscordConfirmationResponse.Unavailable(signal);

        Assert.Equal(
            expected,
            DiscordCaptureRuntime.IsWorkerFailure(response));
    }

    [Theory]
    [InlineData(
        "worker-process-exited",
        CaptureRuntimeState.Recovering)]
    [InlineData(
        "renderer-accessibility-root-unavailable",
        CaptureRuntimeState.ReconnectRequired)]
    [InlineData(
        "message-list-unavailable",
        CaptureRuntimeState.Connecting)]
    [InlineData(
        "request-or-window-validation-failed",
        CaptureRuntimeState.Connecting)]
    public void ClassifiesUnavailableSignalsWithoutFalseReconnect(
        string signal,
        CaptureRuntimeState expected)
    {
        var response = DiscordConfirmationResponse.Unavailable(signal);

        Assert.Equal(
            expected,
            DiscordCaptureRuntime.ClassifyUnavailableState(response));
    }
}
