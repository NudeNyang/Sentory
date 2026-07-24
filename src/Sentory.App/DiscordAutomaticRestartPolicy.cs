using Sentory.Core;

namespace Sentory.App;

internal static class DiscordAutomaticRestartPolicy
{
    public static bool ShouldPrompt(
        bool supportEnabled,
        bool detectionPaused,
        bool repairBusy,
        CaptureRuntimeState state,
        int? currentProcessId,
        int? promptedProcessId) =>
        supportEnabled &&
        !detectionPaused &&
        !repairBusy &&
        state == CaptureRuntimeState.ReconnectRequired &&
        currentProcessId is not null &&
        currentProcessId != promptedProcessId;

    public static TimeSpan GetProcessCheckInterval(
        bool supportEnabled,
        CaptureRuntimeState state) =>
        !supportEnabled
            ? TimeSpan.FromSeconds(30)
            : state == CaptureRuntimeState.Ready
                ? TimeSpan.FromSeconds(10)
                : TimeSpan.FromSeconds(3);
}
