namespace Sentory.App;

using Sentory.Core;

internal readonly record struct UpdateAvailabilityUiPresentation(
    bool ShowInstallAction,
    bool EnableInstallAction);

internal static class UpdateAvailabilityUiPolicy
{
    public static UpdateAvailabilityUiPresentation Resolve(
        string? availableVersion,
        string? currentVersion,
        bool installationInProgress)
    {
        var updateAvailable =
            SemanticVersion.TryParse(availableVersion, out var available) &&
            SemanticVersion.TryParse(currentVersion, out var current) &&
            available.CompareTo(current) > 0;
        return new UpdateAvailabilityUiPresentation(
            updateAvailable,
            updateAvailable && !installationInProgress);
    }
}
