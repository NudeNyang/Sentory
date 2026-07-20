namespace Sentory.App.Tests;

public sealed class StartupPreferencePolicyTests
{
    [Theory]
    [InlineData(false, null, false, true)]
    [InlineData(false, null, true, true)]
    [InlineData(true, null, false, false)]
    [InlineData(true, null, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, true)]
    public void ResolvesNewLegacyAndExplicitPreferences(
        bool settingsFileExisted,
        bool? savedPreference,
        bool registrationEnabled,
        bool expected)
    {
        var resolved = StartupPreferencePolicy.Resolve(
            settingsFileExisted,
            savedPreference,
            registrationEnabled);

        Assert.Equal(expected, resolved);
    }
}
