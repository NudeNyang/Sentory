using Sentory.Diagnostics.Uia;

namespace Sentory.Diagnostics.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void ParseSnapshotReadsPrivacySafeLimits()
    {
        var result = CliOptions.Parse(
        [
            "snapshot",
            "--process", "Discord,KakaoTalk",
            "--view", "control",
            "--max-depth", "12",
            "--max-elements", "900",
            "--output", "snapshot.json"
        ]);

        Assert.Equal(DiagnosticCommand.Snapshot, result.Command);
        Assert.Equal(["Discord", "KakaoTalk"], result.ProcessNames);
        Assert.Equal(UiaTreeView.Control, result.View);
        Assert.Equal(12, result.MaxDepth);
        Assert.Equal(900, result.MaxElements);
        Assert.Equal("snapshot.json", result.OutputPath);
    }

    [Fact]
    public void ParseRejectsUnboundedWatchDuration()
    {
        var exception = Assert.Throws<CliException>(
            () => CliOptions.Parse(["watch", "--seconds", "301"]));

        Assert.Contains("1~300", exception.Message);
    }
}
