namespace Sentory.App.Tests;

public sealed class SentoryBuildIdentityTests
{
    [Theory]
    [InlineData("1.1.3+developers", true)]
    [InlineData("1.1.3+DEVELOPERS", true)]
    [InlineData("1.1.3", false)]
    [InlineData("1.1.3+public", false)]
    [InlineData(null, false)]
    public void DetectsDeveloperMarkerInInformationalVersion(
        string? informationalVersion,
        bool expected)
    {
        Assert.Equal(
            expected,
            SentoryBuildIdentity.IsDeveloperInformationalVersion(
                informationalVersion));
    }
}
