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
    string DatabasePath,
    string ImagesDirectory,
    string LinkPreviewsDirectory)
{
    public string SettingsPath =>
        Path.Combine(RootDirectory, "gallery-settings.json");

    public string LogsDirectory =>
        Path.Combine(RootDirectory, "logs");

    public static SentoryDataPaths FromEnvironmentOrCurrentUser(
        string? overrideRoot)
    {
        if (string.IsNullOrWhiteSpace(overrideRoot))
        {
            return ForCurrentUser();
        }

        if (!Path.IsPathRooted(overrideRoot))
        {
            throw new ArgumentException(
                "데이터 폴더 재정의 경로는 절대 경로여야 합니다.",
                nameof(overrideRoot));
        }

        return ForRoot(overrideRoot);
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

    public static SentoryDataPaths ForRoot(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        return new SentoryDataPaths(
            root,
            Path.Combine(root, "sentory.db"),
            Path.Combine(root, "images"),
            Path.Combine(root, "link-previews"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ImagesDirectory);
        Directory.CreateDirectory(LinkPreviewsDirectory);
        Directory.CreateDirectory(LogsDirectory);
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
