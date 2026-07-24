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

    [Theory]
    [InlineData(
        "message-list-unavailable",
        CaptureRuntimeState.Connecting,
        true,
        false)]
    [InlineData(
        "worker-process-exited",
        CaptureRuntimeState.Recovering,
        true,
        false)]
    [InlineData(
        "renderer-accessibility-root-unavailable",
        CaptureRuntimeState.ReconnectRequired,
        false,
        true)]
    public void PlansAutomaticRefreshBeforeRequestingDiscordRestart(
        string signal,
        CaptureRuntimeState expectedState,
        bool expectedWarmup,
        bool expectedIssue)
    {
        var response = DiscordConfirmationResponse.Unavailable(signal);

        var plan = DiscordCaptureRuntime.PlanUnavailableRecovery(response);

        Assert.Equal(expectedState, plan.State);
        Assert.Equal(expectedWarmup, plan.BeginWarmup);
        Assert.Equal(expectedIssue, plan.ReportIssue);
    }

    [Fact]
    public void InitialWarmupKeepsAThrottledRetryAlive()
    {
        var plan = DiscordCaptureRuntime.PlanWarmupExhaustion(
            CaptureRuntimeState.Connecting,
            reconnectWhenExhausted: false);

        Assert.Equal(CaptureRuntimeState.Connecting, plan.State);
        Assert.True(plan.ContinueWaiting);
        Assert.False(plan.ReportIssue);
        Assert.Equal(TimeSpan.FromSeconds(30), plan.RetryDelay);
    }

    [Fact]
    public void SendFailureEscalatesAfterAutomaticRefreshIsExhausted()
    {
        var plan = DiscordCaptureRuntime.PlanWarmupExhaustion(
            CaptureRuntimeState.Connecting,
            reconnectWhenExhausted: true);

        Assert.Equal(CaptureRuntimeState.ReconnectRequired, plan.State);
        Assert.False(plan.ContinueWaiting);
        Assert.True(plan.ReportIssue);
    }

    [Fact]
    public void RepeatedMessageListFailureKeepsWaitingWithoutLaunchEvidence()
    {
        var plan = DiscordCaptureRuntime.PlanWarmupExhaustion(
            CaptureRuntimeState.Connecting,
            reconnectWhenExhausted: false);

        Assert.Equal(CaptureRuntimeState.Connecting, plan.State);
        Assert.True(plan.ContinueWaiting);
        Assert.False(plan.ReportIssue);
    }

    [Fact]
    public void DefinitiveAccessibilityFailureSurvivesAWindowTransition()
    {
        Assert.Equal(
            CaptureRuntimeState.ReconnectRequired,
            DiscordCaptureRuntime.MergeUnavailableState(
                CaptureRuntimeState.ReconnectRequired,
                CaptureRuntimeState.Connecting));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ProcessChangeRestartsTheFullWarmupBurst(
        bool wakeRequested,
        bool expectedRestart)
    {
        var action = DiscordCaptureRuntime.ResolveAttemptWaitAction(
            wakeRequested);

        Assert.Equal(
            expectedRestart,
            action == DiscordWarmupAttemptWaitAction.RestartBurst);
    }
}
