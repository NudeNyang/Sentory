using System.IO;
using Sentory.Core.Diagnostics;

namespace Sentory.Platform.Windows.Runtime;

internal static class DiscordCaptureTrace
{
    private static readonly string LogPath = ResolveLogPath(
        Environment.GetEnvironmentVariable("SENTORY_DATA_DIR"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string ResolveLogPath(
        string? dataDirectoryOverride,
        string localAppDataDirectory)
    {
        var dataDirectory = string.IsNullOrWhiteSpace(dataDirectoryOverride)
            ? Path.Combine(localAppDataDirectory, "Sentory")
            : Path.GetFullPath(dataDirectoryOverride);

        return Path.Combine(dataDirectory, "logs", "sentory.log");
    }

    public static void Write(string stage, string? detail = null)
    {
        SentoryDiagnosticLogFile.Append(
            LogPath,
            "discord-capture",
            string.IsNullOrWhiteSpace(detail)
                ? stage
                : $"{stage} {detail}");
    }
}
