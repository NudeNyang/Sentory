using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Sentory.App;

internal static class DisplayNamedImageFile
{
    private static readonly HashSet<string> ReservedWindowsNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5",
            "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
            "LPT6", "LPT7", "LPT8", "LPT9"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static string Prepare(
        string sourcePath,
        string displayName,
        string? contentIdentity,
        string? openRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "저장된 사진 파일을 찾지 못했습니다.",
                sourcePath);
        }

        var root = openRoot ?? Path.Combine(
            Path.GetTempPath(),
            "Sentory",
            "opened-images");
        var identity = string.IsNullOrWhiteSpace(contentIdentity)
            ? Path.GetFullPath(sourcePath)
            : contentIdentity;
        var identityDirectory = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
        var directory = Path.Combine(root, identityDirectory);
        Directory.CreateDirectory(directory);

        var safeName = CreateSafeFileName(displayName);
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        var targetPath = Path.Combine(directory, $"{safeName}{extension}");
        if (File.Exists(targetPath) &&
            new FileInfo(targetPath).Length == new FileInfo(sourcePath).Length)
        {
            return targetPath;
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
        return targetPath;
    }

    public static void CleanupOldCopies(
        TimeSpan maximumAge,
        string? openRoot = null,
        DateTimeOffset? currentTime = null)
    {
        if (maximumAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        var root = openRoot ?? Path.Combine(
            Path.GetTempPath(),
            "Sentory",
            "opened-images");
        if (!Directory.Exists(root))
        {
            return;
        }

        var cutoff = (currentTime ?? DateTimeOffset.UtcNow) - maximumAge;
        try
        {
            foreach (var file in Directory.EnumerateFiles(
                         root,
                         "*",
                         SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(
                         root,
                         "*",
                         SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static string CreateSafeFileName(string? displayName)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(displayName?.Length ?? 0);
        var previousWasWhitespace = false;
        foreach (var character in displayName?.Trim() ?? string.Empty)
        {
            if (char.IsControl(character) || invalid.Contains(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                previousWasWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        var result = builder.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(result))
        {
            return "이미지";
        }

        return ReservedWindowsNames.Contains(result)
            ? $"_{result}"
            : result;
    }
}
