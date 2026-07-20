using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordCaptureTraceTests
{
    [Fact]
    public void UsesExplicitDataDirectoryForDiagnostics()
    {
        var dataDirectory = Path.Combine("C:\\", "Sentory-Test-Data");
        var localAppData = Path.Combine("C:\\", "Users", "test", "AppData", "Local");

        var result = DiscordCaptureTrace.ResolveLogDirectory(
            dataDirectory,
            localAppData);

        Assert.Equal(
            Path.Combine(dataDirectory, "diagnostics"),
            result);
    }

    [Fact]
    public void UsesDefaultDataDirectoryWhenOverrideIsMissing()
    {
        var localAppData = Path.Combine("C:\\", "Users", "test", "AppData", "Local");

        var result = DiscordCaptureTrace.ResolveLogDirectory(
            null,
            localAppData);

        Assert.Equal(
            Path.Combine(localAppData, "Sentory", "diagnostics"),
            result);
    }
}
