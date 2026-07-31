using Sentory.Platform.Windows.Runtime;

namespace Sentory.Engine.Bridge;

internal static class DiscordAutomaticRestartPolicy
{
    internal const int CountdownSeconds = 15;

    internal static bool ShouldOffer(
        bool discordSupportEnabled,
        int? processId,
        DiscordAccessibilityArgumentState argumentState) =>
        discordSupportEnabled &&
        processId.HasValue &&
        argumentState == DiscordAccessibilityArgumentState.Missing;
}
