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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
