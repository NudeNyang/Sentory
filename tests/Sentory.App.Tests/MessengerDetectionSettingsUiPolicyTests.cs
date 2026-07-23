namespace Sentory.App.Tests;

public sealed class MessengerDetectionSettingsUiPolicyTests
{
    [Theory]
    [InlineData(false, false, "Disabled")]
    [InlineData(false, true, "Disabled")]
    [InlineData(true, true, "Paused")]
    [InlineData(true, false, "Active")]
    public void GlobalPauseOverridesOnlyEnabledMessengerStatus(
        bool enabled,
        bool globallyPaused,
        string expected)
    {
        var actual = MessengerDetectionSettingsUiPolicy.Resolve(
            enabled,
            globallyPaused);

        Assert.Equal(
            Enum.Parse<MessengerDetectionSettingsState>(expected),
            actual);
    }
}
