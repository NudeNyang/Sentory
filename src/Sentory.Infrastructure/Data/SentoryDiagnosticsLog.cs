using Sentory.Core.Diagnostics;

namespace Sentory.Infrastructure.Data;

public sealed class SentoryDiagnosticsLog
{
    private readonly SentoryDataPaths _paths;

    public SentoryDiagnosticsLog(SentoryDataPaths paths)
    {
        _paths = paths;
        SentoryDiagnosticLogFile.ConsolidateLegacyLogs(
            CurrentLogPath,
            Path.Combine(_paths.LogsDirectory, "sentory.previous.log"),
            Path.Combine(
                _paths.RootDirectory,
                "diagnostics",
                "discord-capture.log"));
    }

    public string CurrentLogPath =>
        Path.Combine(_paths.LogsDirectory, "sentory.log");

    public void Write(
        string category,
        string message,
        Exception? exception = null)
    {
        if (string.IsNullOrWhiteSpace(category) ||
            string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _paths.EnsureDirectories();
        SentoryDiagnosticLogFile.Append(
            CurrentLogPath,
            category,
            message,
            exception);
    }
}
