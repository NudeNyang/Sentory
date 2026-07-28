using Sentory.Core;

namespace Sentory.App;

internal readonly record struct DiscordDetectionUiPresentation(
    bool ShowPassiveStatus,
    bool ShowTrayStatus,
    bool ShowRepairAction,
    CaptureRuntimeState DisplayState);

internal static class DiscordDetectionUiPolicy
{
    public static DiscordDetectionUiPresentation Resolve(
        bool enabled,
        bool processRunning,
        CaptureRuntimeState state,
        bool repairNeeded,
        bool repairBannerDismissed = false)
    {
        if (!enabled || !processRunning)
        {
            return new DiscordDetectionUiPresentation(
                false,
                false,
                false,
                state);
        }

        var showRepairAction =
            repairNeeded || state == CaptureRuntimeState.ReconnectRequired;
        var showPassiveStatus =
            (repairNeeded && repairBannerDismissed) ||
            (!showRepairAction &&
             state is CaptureRuntimeState.Connecting or
                 CaptureRuntimeState.Recovering);

        return new DiscordDetectionUiPresentation(
            showPassiveStatus,
            showPassiveStatus || showRepairAction,
            showRepairAction,
            repairNeeded
                ? CaptureRuntimeState.ReconnectRequired
                : state);
    }
}
