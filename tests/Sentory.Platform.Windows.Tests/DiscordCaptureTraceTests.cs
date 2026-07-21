using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordCaptureTraceTests
{
    [Fact]
    public void UsesExplicitDataDirectoryForUnifiedLog()
    {
        var dataDirectory = Path.Combine("C:\\", "Sentory-Test-Data");
        var localAppData = Path.Combine("C:\\", "Users", "test", "AppData", "Local");

        var result = DiscordCaptureTrace.ResolveLogPath(
            dataDirectory,
            localAppData);

        Assert.Equal(
            Path.Combine(dataDirectory, "logs", "sentory.log"),
            result);
    }

    [Fact]
    public void UsesDefaultDataDirectoryForUnifiedLogWhenOverrideIsMissing()
    {
        var localAppData = Path.Combine("C:\\", "Users", "test", "AppData", "Local");

        var result = DiscordCaptureTrace.ResolveLogPath(
            null,
            localAppData);

        Assert.Equal(
            Path.Combine(
                localAppData,
                "Sentory",
                "logs",
                "sentory.log"),
            result);
    }
}
