using Sentory.Core;

namespace Sentory.App.Tests;

public sealed class DiscordStartupPreparationPolicyTests
{
    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    public void DoesNothingWhenDiscordSupportCannotBePrepared(
        bool supportEnabled,
        bool launcherInstalled,
        bool discordRunning,
        bool accessibilityPrepared)
    {
        Assert.Equal(
            DiscordStartupPreparationAction.None,
            DiscordStartupPreparationPolicy.Resolve(
                supportEnabled,
                launcherInstalled,
                discordRunning,
                accessibilityPrepared));
    }

    [Fact]
    public void StartsDiscordWhenItIsNotRunning()
    {
        Assert.Equal(
            DiscordStartupPreparationAction.StartDiscord,
            DiscordStartupPreparationPolicy.Resolve(
                supportEnabled: true,
                launcherInstalled: true,
                discordRunning: false,
                accessibilityPrepared: false));
    }

    [Fact]
    public void RequiresRestartWhenFirstConnectionFindsDiscordRunning()
    {
        Assert.Equal(
            DiscordStartupPreparationAction.RequireRestart,
            DiscordStartupPreparationPolicy.Resolve(
                supportEnabled: true,
                launcherInstalled: true,
                discordRunning: true,
                accessibilityPrepared: false));
    }

    [Fact]
    public void KeepsPreparedRunningDiscordUntouched()
    {
        Assert.Equal(
            DiscordStartupPreparationAction.None,
            DiscordStartupPreparationPolicy.Resolve(
                supportEnabled: true,
                launcherInstalled: true,
                discordRunning: true,
                accessibilityPrepared: true));
    }

    [Theory]
    [InlineData(CaptureRuntimeState.Connecting)]
    [InlineData(CaptureRuntimeState.Recovering)]
    public void KeepsRestartRequirementDuringTransientRuntimeStates(
        CaptureRuntimeState state)
    {
        Assert.True(
            DiscordStartupPreparationPolicy.ResolveRepairNeeded(
                currentlyRequired: true,
                state));
    }

    [Fact]
    public void ClearsRestartRequirementOnlyAfterRuntimeIsReady()
    {
        Assert.False(
            DiscordStartupPreparationPolicy.ResolveRepairNeeded(
                currentlyRequired: true,
                CaptureRuntimeState.Ready));
    }

    [Fact]
    public void RuntimeReconnectFailureRequiresRestart()
    {
        Assert.True(
            DiscordStartupPreparationPolicy.ResolveRepairNeeded(
                currentlyRequired: false,
                CaptureRuntimeState.ReconnectRequired));
    }

    [Fact]
    public void RestartRequestImmediatelySwitchesToConnectingPresentation()
    {
        var state = DiscordStartupPreparationPolicy.RestartStarted;
        var presentation = DiscordDetectionUiPolicy.Resolve(
            enabled: true,
            state.DetectionState,
            state.RepairNeeded);

        Assert.Equal(CaptureRuntimeState.Connecting, state.DetectionState);
        Assert.False(state.RepairNeeded);
        Assert.True(presentation.ShowPassiveStatus);
        Assert.False(presentation.ShowRepairAction);
    }

    [Fact]
    public void FailedRestartRestoresTheRepairPresentation()
    {
        var state = DiscordStartupPreparationPolicy.RestartFailed;

        Assert.Equal(
            CaptureRuntimeState.ReconnectRequired,
            state.DetectionState);
        Assert.True(state.RepairNeeded);
    }

    [Fact]
    public void ClearsDiscordRecoveryIssueWhenRuntimeBecomesReady()
    {
        Assert.True(
            DiscordStartupPreparationPolicy.ShouldClearRuntimeIssue(
                "discord-detection-unavailable",
                CaptureRuntimeState.Ready));
    }

    [Theory]
    [InlineData("discord-detection-unavailable", CaptureRuntimeState.Connecting)]
    [InlineData("discord-detection-unavailable", CaptureRuntimeState.Recovering)]
    [InlineData("auto-cleanup-failed", CaptureRuntimeState.Ready)]
    [InlineData(null, CaptureRuntimeState.Ready)]
    public void KeepsRuntimeIssueUntilItsMatchingRecoveryCompletes(
        string? issueCode,
        CaptureRuntimeState state)
    {
        Assert.False(
            DiscordStartupPreparationPolicy.ShouldClearRuntimeIssue(
                issueCode,
                state));
    }
}
