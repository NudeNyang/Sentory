namespace Sentory.App.Tests;

public sealed class SentoryBuildIdentityTests
{
    [Theory]
    [InlineData("1.1.32+developers", true)]
    [InlineData("1.1.32+DEVELOPERS", true)]
    [InlineData("1.1.32", false)]
    [InlineData("1.1.32+public", false)]
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
