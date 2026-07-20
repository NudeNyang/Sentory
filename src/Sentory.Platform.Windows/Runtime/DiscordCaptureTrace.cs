using System.IO;
using System.Text;

namespace Sentory.Platform.Windows.Runtime;

internal static class DiscordCaptureTrace
{
    private const long MaximumLogBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static readonly string LogDirectory = ResolveLogDirectory(
        Environment.GetEnvironmentVariable("SENTORY_DATA_DIR"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    private static readonly string LogPath = Path.Combine(
        LogDirectory,
        "discord-capture.log");

    internal static string ResolveLogDirectory(
        string? dataDirectoryOverride,
        string localAppDataDirectory)
    {
        var dataDirectory = string.IsNullOrWhiteSpace(dataDirectoryOverride)
            ? Path.Combine(localAppDataDirectory, "Sentory")
            : Path.GetFullPath(dataDirectoryOverride);

        return Path.Combine(dataDirectory, "diagnostics");
    }

    public static void Write(string stage, string? detail = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                if (File.Exists(LogPath) &&
                    new FileInfo(LogPath).Length >= MaximumLogBytes)
                {
                    File.Delete(LogPath);
                }

                var line = new StringBuilder()
                    .Append(DateTimeOffset.UtcNow.ToString("O"))
                    .Append(' ')
                    .Append(stage);
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    line.Append(' ').Append(detail);
                }

                line.AppendLine();
                File.AppendAllText(LogPath, line.ToString(), Encoding.UTF8);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
