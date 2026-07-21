namespace Sentory.App.Tests;

public sealed class SentoryBuildIdentityTests
{
    [Theory]
    [InlineData("1.4.0+developers", true)]
    [InlineData("1.4.0+DEVELOPERS", true)]
    [InlineData("1.4.0", false)]
    [InlineData("1.4.0+public", false)]
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
