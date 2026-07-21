using Sentory.Core;

namespace Sentory.App;

internal enum DiscordStartupPreparationAction
{
    None,
    StartDiscord,
    RequireRestart
}

internal static class DiscordStartupPreparationPolicy
{
    public static DiscordStartupPreparationAction Resolve(
        bool supportEnabled,
        bool launcherInstalled,
        bool discordRunning,
        bool accessibilityPrepared)
    {
        if (!supportEnabled || !launcherInstalled)
        {
            return DiscordStartupPreparationAction.None;
        }

        if (!discordRunning)
        {
            return DiscordStartupPreparationAction.StartDiscord;
        }

        return accessibilityPrepared
            ? DiscordStartupPreparationAction.None
            : DiscordStartupPreparationAction.RequireRestart;
    }

    public static bool ResolveRepairNeeded(
        bool currentlyRequired,
        CaptureRuntimeState state) =>
        state switch
        {
            CaptureRuntimeState.Ready => false,
            CaptureRuntimeState.ReconnectRequired => true,
            _ => currentlyRequired
        };

    public static bool ShouldClearRuntimeIssue(
        string? issueCode,
        CaptureRuntimeState state) =>
        state == CaptureRuntimeState.Ready &&
        string.Equals(
            issueCode,
            "discord-detection-unavailable",
            StringComparison.Ordinal);
}
