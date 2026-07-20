using Sentory.Core;

namespace Sentory.App;

internal readonly record struct DiscordDetectionUiPresentation(
    bool ShowPassiveStatus,
    bool ShowTrayStatus,
    bool ShowRepairAction);

internal static class DiscordDetectionUiPolicy
{
    public static DiscordDetectionUiPresentation Resolve(
        bool enabled,
        CaptureRuntimeState state,
        bool repairNeeded)
    {
        if (!enabled)
        {
            return new DiscordDetectionUiPresentation(false, false, false);
        }

        var showRepairAction =
            repairNeeded || state == CaptureRuntimeState.ReconnectRequired;
        var showPassiveStatus =
            !showRepairAction && state == CaptureRuntimeState.Recovering;

        return new DiscordDetectionUiPresentation(
            showPassiveStatus,
            showPassiveStatus || showRepairAction,
            showRepairAction);
    }
}
