using Sentory.Core;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.App;

internal static class DiscordAutomaticRestartPolicy
{
    public static bool ShouldPrompt(
        bool supportEnabled,
        bool detectionPaused,
        bool repairBusy,
        CaptureRuntimeState state,
        DiscordAccessibilityArgumentState argumentState,
        int? currentProcessId,
        int? promptedProcessId) =>
        supportEnabled &&
        !detectionPaused &&
        !repairBusy &&
        state == CaptureRuntimeState.ReconnectRequired &&
        argumentState != DiscordAccessibilityArgumentState.Enabled &&
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

    public static bool ShouldPromptImmediately(
        DiscordAccessibilityArgumentState argumentState) =>
        argumentState == DiscordAccessibilityArgumentState.Missing;
}
