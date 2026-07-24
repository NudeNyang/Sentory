using Sentory.Core;

namespace Sentory.App.Tests;

public sealed class DiscordDetectionUiPolicyTests
{
    [Theory]
    [InlineData(CaptureRuntimeState.Connecting, true, true, false)]
    [InlineData(CaptureRuntimeState.Ready, false, false, false)]
    [InlineData(CaptureRuntimeState.Recovering, true, true, false)]
    [InlineData(CaptureRuntimeState.ReconnectRequired, false, true, true)]
    public void ShowsOnlyActionableOrRecoveryStates(
        CaptureRuntimeState state,
        bool showPassiveStatus,
        bool showTrayStatus,
        bool showRepairAction)
    {
        var presentation = DiscordDetectionUiPolicy.Resolve(
            enabled: true,
            state,
            repairNeeded: false);

        Assert.Equal(showPassiveStatus, presentation.ShowPassiveStatus);
        Assert.Equal(showTrayStatus, presentation.ShowTrayStatus);
        Assert.Equal(showRepairAction, presentation.ShowRepairAction);
    }

    [Fact]
    public void ExplicitRepairNeedOverridesConnectingState()
    {
        var presentation = DiscordDetectionUiPolicy.Resolve(
            enabled: true,
            CaptureRuntimeState.Connecting,
            repairNeeded: true);

        Assert.False(presentation.ShowPassiveStatus);
        Assert.True(presentation.ShowTrayStatus);
        Assert.True(presentation.ShowRepairAction);
    }

    [Fact]
    public void DismissedRepairBannerLeavesCompactReconnectStatus()
    {
        var presentation = DiscordDetectionUiPolicy.Resolve(
            enabled: true,
            CaptureRuntimeState.Connecting,
            repairNeeded: true,
            repairBannerDismissed: true);

        Assert.True(presentation.ShowPassiveStatus);
        Assert.True(presentation.ShowTrayStatus);
        Assert.True(presentation.ShowRepairAction);
        Assert.Equal(
            CaptureRuntimeState.ReconnectRequired,
            presentation.DisplayState);
    }

    [Fact]
    public void DisabledDetectionHidesEveryDiscordStatusAction()
    {
        var presentation = DiscordDetectionUiPolicy.Resolve(
            enabled: false,
            CaptureRuntimeState.ReconnectRequired,
            repairNeeded: true);

        Assert.False(presentation.ShowPassiveStatus);
        Assert.False(presentation.ShowTrayStatus);
        Assert.False(presentation.ShowRepairAction);
    }
}
