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
        CaptureRuntimeState state,
        bool repairNeeded,
        bool repairBannerDismissed = false)
    {
        if (!enabled)
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
