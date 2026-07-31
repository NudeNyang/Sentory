using System.IO;

namespace Sentory.Infrastructure.Data;

public enum DesktopPlatform
{
    Windows,
    MacOS,
    Linux
}

public sealed record SentoryDataPaths(
    string RootDirectory,
    string LocalDataDirectory,
    string DatabasePath,
    string ImagesDirectory,
    string LinkPreviewsDirectory)
{
    public string SettingsPath =>
        Path.Combine(RootDirectory, "gallery-settings.json");

    public string LogsDirectory =>
        Path.Combine(LocalDataDirectory, "logs");

    public string OcrModelsDirectory =>
        Path.Combine(LocalDataDirectory, "ocr-models");

    public static SentoryDataPaths FromEnvironmentOrCurrentUser(
        string? overrideRoot,
        string? overrideLocalDataRoot = null)
    {
        if (string.IsNullOrWhiteSpace(overrideRoot))
        {
            var current = ForCurrentUser();
            return string.IsNullOrWhiteSpace(overrideLocalDataRoot)
                ? current
                : ForRoot(current.RootDirectory, overrideLocalDataRoot);
        }

        if (!Path.IsPathRooted(overrideRoot))
        {
            throw new ArgumentException(
                "데이터 폴더 재정의 경로는 절대 경로여야 합니다.",
                nameof(overrideRoot));
        }

        return ForRoot(overrideRoot, overrideLocalDataRoot);
    }

    public static SentoryDataPaths ForCurrentUser()
    {
        var platform = GetCurrentPlatform();
        return ForPlatform(
            platform,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
    }

    public static SentoryDataPaths ForPlatform(
        DesktopPlatform platform,
        string homeDirectory,
        string? localApplicationData = null,
        string? xdgDataHome = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);

        var root = platform switch
        {
            DesktopPlatform.Windows => Path.Combine(
                RequireDirectory(
                    localApplicationData,
                    nameof(localApplicationData)),
                "Sentory"),
            DesktopPlatform.MacOS => Path.Combine(
                homeDirectory,
                "Library",
                "Application Support",
                "Sentory"),
            DesktopPlatform.Linux =>
                Path.Combine(
                    ResolveLinuxDataDirectory(homeDirectory, xdgDataHome),
                    "Sentory"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(platform),
                platform,
                "지원하지 않는 데스크톱 플랫폼입니다.")
        };

        return ForRoot(root);
    }

    public static SentoryDataPaths ForRoot(
        string rootDirectory,
        string? localDataDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        var localData = string.IsNullOrWhiteSpace(localDataDirectory)
            ? Path.Combine(root, "local-data")
            : Path.GetFullPath(localDataDirectory);
        return new SentoryDataPaths(
            root,
            localData,
            Path.Combine(root, "sentory.db"),
            Path.Combine(root, "images"),
            Path.Combine(localData, "link-previews"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LocalDataDirectory);
        Directory.CreateDirectory(ImagesDirectory);
        Directory.CreateDirectory(LinkPreviewsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(OcrModelsDirectory);
    }

    public string? TryResolveContentPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var normalized = relativePath.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);
        var linkPreviewPrefix = $"link-previews{Path.DirectorySeparatorChar}";
        var isLinkPreview = normalized.StartsWith(
            linkPreviewPrefix,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
        var baseDirectory = isLinkPreview
            ? LocalDataDirectory
            : RootDirectory;
        var root = Path.GetFullPath(baseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(baseDirectory, normalized));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(root, comparison))
        {
            return null;
        }

        if (!isLinkPreview || File.Exists(target))
        {
            return target;
        }

        var legacyRoot = Path.GetFullPath(RootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var legacyTarget = Path.GetFullPath(
            Path.Combine(RootDirectory, normalized));
        return legacyTarget.StartsWith(legacyRoot, comparison) &&
            File.Exists(legacyTarget)
            ? legacyTarget
            : target;
    }

    private static DesktopPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return DesktopPlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return DesktopPlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return DesktopPlatform.Linux;
        }

        throw new PlatformNotSupportedException(
            "Sentory가 지원하는 데스크톱 플랫폼이 아닙니다.");
    }

    private static string ResolveLinuxDataDirectory(
        string homeDirectory,
        string? xdgDataHome)
    {
        if (!string.IsNullOrWhiteSpace(xdgDataHome) &&
            Path.IsPathRooted(xdgDataHome))
        {
            return xdgDataHome;
        }

        return Path.Combine(homeDirectory, ".local", "share");
    }

    private static string RequireDirectory(
        string? directory,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "데이터 디렉터리가 필요합니다.",
                parameterName);
        }

        return directory;
    }
}
