namespace Sentory.App;

internal enum MessengerDetectionSettingsState
{
    Disabled,
    Paused,
    Active
}

internal static class MessengerDetectionSettingsUiPolicy
{
    public static MessengerDetectionSettingsState Resolve(
        bool enabled,
        bool globallyPaused)
    {
        if (!enabled)
        {
            return MessengerDetectionSettingsState.Disabled;
        }

        return globallyPaused
            ? MessengerDetectionSettingsState.Paused
            : MessengerDetectionSettingsState.Active;
    }
}
