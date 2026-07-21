using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Tests;

public sealed class SentoryDiagnosticsLogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sentory-log-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void WriteCreatesSanitizedLocalLogEntry()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var log = new SentoryDiagnosticsLog(paths);

        log.Write("capture\tissue", "first line\r\nsecond line");

        var line = Assert.Single(File.ReadAllLines(log.CurrentLogPath));
        Assert.Contains("capture issue", line);
        Assert.Contains("first line  second line", line);
        Assert.DoesNotContain('\t', line.Split('\t')[1]);
        Assert.StartsWith(DateTimeOffset.Now.Year.ToString(), line);
    }

    [Fact]
    public void ConstructorConsolidatesLegacyLogsIntoOneCurrentFile()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        paths.EnsureDirectories();
        var previousPath = Path.Combine(
            paths.LogsDirectory,
            "sentory.previous.log");
        var legacyDirectory = Path.Combine(_root, "diagnostics");
        var legacyDiscordPath = Path.Combine(
            legacyDirectory,
            "discord-capture.log");
        Directory.CreateDirectory(legacyDirectory);
        File.WriteAllText(previousPath, "old app entry");
        File.WriteAllText(legacyDiscordPath, "old discord entry");

        var log = new SentoryDiagnosticsLog(paths);

        var content = File.ReadAllText(log.CurrentLogPath);
        Assert.Contains("old app entry", content);
        Assert.Contains("old discord entry", content);
        Assert.False(File.Exists(previousPath));
        Assert.False(File.Exists(legacyDiscordPath));
        Assert.Equal(
            log.CurrentLogPath,
            Assert.Single(
                Directory.GetFiles(
                    _root,
                    "*.log",
                    SearchOption.AllDirectories)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
