using Sentory.Diagnostics.Uia;

namespace Sentory.Diagnostics.Tests;

public sealed class PowerShellUiaProbeTests
{
    [Fact]
    public async Task CaptureReturnsPrivacySafeEmptyResultForMissingProcess()
    {
        var options = CliOptions.Parse(
        [
            "snapshot",
            "--process", "SentoryProcessThatDoesNotExist",
            "--view", "raw",
            "--max-elements", "10"
        ]);

        var result = await PowerShellUiaProbe.CaptureAsync(
            options,
            TimeSpan.FromSeconds(10));

        Assert.Equal(
            "Windows PowerShell 5.1 / .NET Framework UI Automation",
            result.GetProperty("runtime").GetString());
        Assert.Equal(
            0,
            result.GetProperty("snapshots").GetArrayLength());
        Assert.Contains(
            "No names, values, text",
            result.GetProperty("privacyMode").GetString());
    }
}
