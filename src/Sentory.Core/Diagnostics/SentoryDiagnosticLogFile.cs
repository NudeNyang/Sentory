using System.Text;

namespace Sentory.Core.Diagnostics;

public static class SentoryDiagnosticLogFile
{
    private const long MaximumLogBytes = 1024 * 1024;
    private const long TrimmedLogBytes = 768 * 1024;
    private const int MaximumFieldCharacters = 8192;
    private const string MutexName = "Sentory.DiagnosticsLogFile.v1";
    private static readonly object Gate = new();
    private static readonly Mutex CrossProcessGate = new(false, MutexName);

    public static void Append(
        string logPath,
        string category,
        string message,
        Exception? exception = null)
    {
        if (string.IsNullOrWhiteSpace(logPath) ||
            string.IsNullOrWhiteSpace(category) ||
            string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ExecuteWithGate(() =>
        {
            var directory = Path.GetDirectoryName(logPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            TrimIfNeeded(logPath);
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
                logPath,
                line.AppendLine().ToString(),
                Encoding.UTF8);
        });
    }

    public static void ConsolidateLegacyLogs(
        string logPath,
        params string[] legacyPaths)
    {
        if (string.IsNullOrWhiteSpace(logPath) ||
            legacyPaths.Length == 0)
        {
            return;
        }

        ExecuteWithGate(() =>
        {
            var existingLegacyPaths = legacyPaths
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    !string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(logPath),
                        StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (existingLegacyPaths.Length == 0)
            {
                return;
            }

            var allPaths = existingLegacyPaths
                .Prepend(logPath)
                .Where(File.Exists)
                .ToArray();
            var entries = allPaths
                .SelectMany((path, sourceIndex) =>
                    File.ReadLines(path)
                        .Select((line, lineIndex) => new LogEntry(
                            line,
                            ParseTimestamp(line),
                            sourceIndex,
                            lineIndex)))
                .OrderBy(entry => entry.Timestamp)
                .ThenBy(entry => entry.SourceIndex)
                .ThenBy(entry => entry.LineIndex)
                .Select(entry => entry.Line)
                .ToArray();
            var directory = Path.GetDirectoryName(logPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            File.WriteAllLines(
                logPath,
                KeepNewestWithin(entries, MaximumLogBytes),
                Encoding.UTF8);
            foreach (var legacyPath in existingLegacyPaths)
            {
                File.Delete(legacyPath);
                RemoveEmptyDirectory(Path.GetDirectoryName(legacyPath));
            }
        });
    }

    private static void ExecuteWithGate(Action action)
    {
        try
        {
            lock (Gate)
            {
                var acquired = false;
                try
                {
                    try
                    {
                        acquired = CrossProcessGate.WaitOne(
                            TimeSpan.FromSeconds(2));
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                    }

                    if (acquired)
                    {
                        action();
                    }
                }
                finally
                {
                    if (acquired)
                    {
                        CrossProcessGate.ReleaseMutex();
                    }
                }
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TrimIfNeeded(string logPath)
    {
        if (!File.Exists(logPath) ||
            new FileInfo(logPath).Length < MaximumLogBytes)
        {
            return;
        }

        var lines = File.ReadAllLines(logPath);
        File.WriteAllLines(
            logPath,
            KeepNewestWithin(lines, TrimmedLogBytes),
            Encoding.UTF8);
    }

    private static IReadOnlyList<string> KeepNewestWithin(
        IReadOnlyList<string> lines,
        long maximumBytes)
    {
        var kept = new List<string>();
        long bytes = 0;
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            var lineBytes = Encoding.UTF8.GetByteCount(lines[index]) +
                            Environment.NewLine.Length;
            if (bytes + lineBytes > maximumBytes)
            {
                break;
            }

            kept.Add(lines[index]);
            bytes += lineBytes;
        }

        kept.Reverse();
        return kept;
    }

    private static DateTimeOffset ParseTimestamp(string line)
    {
        var separator = line.IndexOfAny(['\t', ' ']);
        var value = separator >= 0 ? line[..separator] : line;
        return DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp
            : DateTimeOffset.MinValue;
    }

    private static string Sanitize(string value)
    {
        var sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        return sanitized.Length <= MaximumFieldCharacters
            ? sanitized
            : sanitized[..MaximumFieldCharacters];
    }

    private static void RemoveEmptyDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) ||
            !Directory.Exists(directory) ||
            Directory.EnumerateFileSystemEntries(directory).Any())
        {
            return;
        }

        Directory.Delete(directory);
    }

    private sealed record LogEntry(
        string Line,
        DateTimeOffset Timestamp,
        int SourceIndex,
        int LineIndex);
}
