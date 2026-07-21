namespace Sentory.App.Tests;

public sealed class UpdateAvailabilityUiPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HidesManualInstallActionWithoutAnAvailableVersion(string? version)
    {
        var presentation = UpdateAvailabilityUiPolicy.Resolve(
            version,
            currentVersion: "1.1.32",
            installationInProgress: false);

        Assert.False(presentation.ShowInstallAction);
        Assert.False(presentation.EnableInstallAction);
    }

    [Fact]
    public void ShowsManualInstallActionWhileAnUpdateIsWaiting()
    {
        var presentation = UpdateAvailabilityUiPolicy.Resolve(
            "1.1.33",
            currentVersion: "1.1.32",
            installationInProgress: false);

        Assert.True(presentation.ShowInstallAction);
        Assert.True(presentation.EnableInstallAction);
    }

    [Fact]
    public void KeepsActionVisibleButDisablesItDuringInstallation()
    {
        var presentation = UpdateAvailabilityUiPolicy.Resolve(
            "1.1.33",
            currentVersion: "1.1.32",
            installationInProgress: true);

        Assert.True(presentation.ShowInstallAction);
        Assert.False(presentation.EnableInstallAction);
    }

    [Theory]
    [InlineData("1.1.32")]
    [InlineData("1.1.31")]
    [InlineData("release")]
    public void HidesStaleOrInvalidAvailableVersion(string availableVersion)
    {
        var presentation = UpdateAvailabilityUiPolicy.Resolve(
            availableVersion,
            currentVersion: "1.1.32",
            installationInProgress: false);

        Assert.False(presentation.ShowInstallAction);
        Assert.False(presentation.EnableInstallAction);
    }
}
