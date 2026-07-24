using Sentory.Core;

namespace Sentory.App.Tests;

public sealed class DiscordAutomaticRestartPolicyTests
{
    [Theory]
    [InlineData(CaptureRuntimeState.Connecting, false)]
    [InlineData(CaptureRuntimeState.Ready, false)]
    [InlineData(CaptureRuntimeState.Recovering, false)]
    [InlineData(CaptureRuntimeState.ReconnectRequired, true)]
    public void PromptsOnlyForAnActionableCurrentProcess(
        CaptureRuntimeState state,
        bool expected)
    {
        Assert.Equal(
            expected,
            DiscordAutomaticRestartPolicy.ShouldPrompt(
                supportEnabled: true,
                detectionPaused: false,
                repairBusy: false,
                state,
                currentProcessId: 42,
                promptedProcessId: null));
    }

    [Fact]
    public void DoesNotPromptTwiceForTheSameDiscordProcess()
    {
        Assert.False(
            DiscordAutomaticRestartPolicy.ShouldPrompt(
                supportEnabled: true,
                detectionPaused: false,
                repairBusy: false,
                CaptureRuntimeState.ReconnectRequired,
                currentProcessId: 42,
                promptedProcessId: 42));
    }

    [Fact]
    public void DoesNotPromptWhileDetectionIsPaused()
    {
        Assert.False(
            DiscordAutomaticRestartPolicy.ShouldPrompt(
                supportEnabled: true,
                detectionPaused: true,
                repairBusy: false,
                CaptureRuntimeState.ReconnectRequired,
                currentProcessId: 42,
                promptedProcessId: null));
    }

    [Theory]
    [InlineData(true, CaptureRuntimeState.Connecting, 3)]
    [InlineData(true, CaptureRuntimeState.Ready, 10)]
    [InlineData(false, CaptureRuntimeState.Connecting, 30)]
    public void UsesOnlyLightweightProcessChecksAfterConnection(
        bool supportEnabled,
        CaptureRuntimeState state,
        int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            DiscordAutomaticRestartPolicy.GetProcessCheckInterval(
                supportEnabled,
                state));
    }
}
