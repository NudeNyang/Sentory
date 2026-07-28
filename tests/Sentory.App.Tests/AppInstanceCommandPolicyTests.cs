using Sentory.App;

namespace Sentory.App.Tests;

public sealed class AppInstanceCommandPolicyTests
{
    [Theory]
    [InlineData("--request-shutdown")]
    [InlineData("--REQUEST-SHUTDOWN")]
    public void ShutdownRequestIsCaseInsensitive(string argument)
    {
        Assert.True(
            AppInstanceCommandPolicy.IsShutdownRequest([argument]));
    }

    [Fact]
    public void UnrelatedArgumentsDoNotRequestShutdown()
    {
        Assert.False(
            AppInstanceCommandPolicy.IsShutdownRequest(
                ["--verify-installation"]));
    }
}
