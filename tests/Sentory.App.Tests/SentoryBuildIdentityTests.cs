namespace Sentory.App.Tests;

public sealed class SentoryBuildIdentityTests
{
    [Theory]
    [InlineData("1.3.33+developers", true)]
    [InlineData("1.3.33+DEVELOPERS", true)]
    [InlineData("1.3.33", false)]
    [InlineData("1.3.33+public", false)]
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
