using System.IO;
using System.Text;

namespace Sentory.Platform.Windows.Runtime;

internal static class DiscordCaptureTrace
{
    private const long MaximumLogBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sentory",
        "diagnostics");
    private static readonly string LogPath = Path.Combine(
        LogDirectory,
        "discord-capture.log");

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
