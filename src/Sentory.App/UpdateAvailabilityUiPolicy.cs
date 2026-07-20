namespace Sentory.App;

internal readonly record struct UpdateAvailabilityUiPresentation(
    bool ShowInstallAction,
    bool EnableInstallAction);

internal static class UpdateAvailabilityUiPolicy
{
    public static UpdateAvailabilityUiPresentation Resolve(
        string? availableVersion,
        bool installationInProgress)
    {
        var updateAvailable = !string.IsNullOrWhiteSpace(availableVersion);
        return new UpdateAvailabilityUiPresentation(
            updateAvailable,
            updateAvailable && !installationInProgress);
    }
}
