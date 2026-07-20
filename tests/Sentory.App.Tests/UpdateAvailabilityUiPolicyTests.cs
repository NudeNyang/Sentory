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
            installationInProgress: false);

        Assert.False(presentation.ShowInstallAction);
        Assert.False(presentation.EnableInstallAction);
    }

    [Fact]
    public void ShowsManualInstallActionWhileAnUpdateIsWaiting()
    {
        var presentation = UpdateAvailabilityUiPolicy.Resolve(
            "1.1.1",
            installationInProgress: false);

        Assert.True(presentation.ShowInstallAction);
        Assert.True(presentation.EnableInstallAction);
    }

    [Fact]
    public void KeepsActionVisibleButDisablesItDuringInstallation()
    {
        var presentation = UpdateAvailabilityUiPolicy.Resolve(
            "1.1.1",
            installationInProgress: true);

        Assert.True(presentation.ShowInstallAction);
        Assert.False(presentation.EnableInstallAction);
    }
}
