using System.Text;

namespace Sentory.Infrastructure.Data;

public sealed class SentoryDiagnosticsLog
{
    private const long MaximumLogBytes = 1024 * 1024;
    private readonly SentoryDataPaths _paths;
    private readonly object _gate = new();

    public SentoryDiagnosticsLog(SentoryDataPaths paths)
    {
        _paths = paths;
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

        try
        {
            lock (_gate)
            {
                _paths.EnsureDirectories();
                RotateIfNeeded();
                var line = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("O"))
                    .Append('\t')
                    .Append(Sanitize(category))
                    .Append('\t')
                    .Append(Sanitize(message));
                if (exception is not null)
                {
                    line.Append('\t')
                        .Append(exception.GetType().Name)
                        .Append(": ")
                        .Append(Sanitize(exception.Message));
                }

                File.AppendAllText(
                    CurrentLogPath,
                    line.AppendLine().ToString(),
                    Encoding.UTF8);
            }
        }
        catch (Exception logException)
            when (logException is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(CurrentLogPath) ||
            new FileInfo(CurrentLogPath).Length < MaximumLogBytes)
        {
            return;
        }

        var previousPath = Path.Combine(
            _paths.LogsDirectory,
            "sentory.previous.log");
        File.Move(CurrentLogPath, previousPath, overwrite: true);
    }

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
}
