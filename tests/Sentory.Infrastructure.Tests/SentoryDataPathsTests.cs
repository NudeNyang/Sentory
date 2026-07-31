using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Tests;

public sealed class SentoryDataPathsTests
{
    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Path.Tests",
        "Home");

    [Fact]
    public void WindowsUsesLocalApplicationData()
    {
        var localAppData = Path.Combine(_home, "LocalAppData");

        var paths = SentoryDataPaths.ForPlatform(
            DesktopPlatform.Windows,
            _home,
            localAppData);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(localAppData, "Sentory")),
            paths.RootDirectory);
        Assert.Equal(
            Path.Combine(paths.RootDirectory, "local-data"),
            paths.LocalDataDirectory);
        Assert.Equal(
            Path.Combine(paths.LocalDataDirectory, "link-previews"),
            paths.LinkPreviewsDirectory);
    }

    [Fact]
    public void MacOsUsesApplicationSupport()
    {
        var paths = SentoryDataPaths.ForPlatform(
            DesktopPlatform.MacOS,
            _home);

        Assert.Equal(
            Path.GetFullPath(
                Path.Combine(
                    _home,
                    "Library",
                    "Application Support",
                    "Sentory")),
            paths.RootDirectory);
    }

    [Fact]
    public void LinuxUsesAbsoluteXdgDataHome()
    {
        var xdgDataHome = Path.Combine(_home, "xdg-data");

        var paths = SentoryDataPaths.ForPlatform(
            DesktopPlatform.Linux,
            _home,
            xdgDataHome: xdgDataHome);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(xdgDataHome, "Sentory")),
            paths.RootDirectory);
    }

    [Fact]
    public void LinuxFallsBackToLocalShareForRelativeXdgPath()
    {
        var paths = SentoryDataPaths.ForPlatform(
            DesktopPlatform.Linux,
            _home,
            xdgDataHome: "relative-data");

        Assert.Equal(
            Path.GetFullPath(
                Path.Combine(_home, ".local", "share", "Sentory")),
            paths.RootDirectory);
    }

    [Fact]
    public void AbsoluteEnvironmentOverrideUsesIsolatedDataRoot()
    {
        var overrideRoot = Path.Combine(_home, "portable-check");

        var paths = SentoryDataPaths.FromEnvironmentOrCurrentUser(
            overrideRoot);

        Assert.Equal(Path.GetFullPath(overrideRoot), paths.RootDirectory);
        Assert.Equal(
            Path.Combine(paths.RootDirectory, "sentory.db"),
            paths.DatabasePath);
        Assert.Equal(
            Path.Combine(paths.RootDirectory, "local-data"),
            paths.LocalDataDirectory);
    }

    [Fact]
    public void SeparateLocalDataRootKeepsDurableFilesInUserRoot()
    {
        var userRoot = Path.Combine(_home, "user-data");
        var localRoot = Path.Combine(_home, "package-local-data");

        var paths = SentoryDataPaths.ForRoot(userRoot, localRoot);

        Assert.Equal(Path.GetFullPath(userRoot), paths.RootDirectory);
        Assert.Equal(Path.GetFullPath(localRoot), paths.LocalDataDirectory);
        Assert.Equal(Path.Combine(userRoot, "sentory.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(userRoot, "images"), paths.ImagesDirectory);
        Assert.Equal(
            Path.Combine(localRoot, "link-previews"),
            paths.LinkPreviewsDirectory);
        Assert.Equal(Path.Combine(localRoot, "logs"), paths.LogsDirectory);
        Assert.Equal(
            Path.Combine(localRoot, "ocr-models"),
            paths.OcrModelsDirectory);
    }

    [Fact]
    public void ContentResolverMapsPreviewCacheToLocalDataRoot()
    {
        var userRoot = Path.Combine(_home, "resolver-user-data");
        var localRoot = Path.Combine(_home, "resolver-local-data");
        var paths = SentoryDataPaths.ForRoot(userRoot, localRoot);

        Assert.Equal(
            Path.Combine(localRoot, "link-previews", "cover.png"),
            paths.TryResolveContentPath(
                Path.Combine("link-previews", "cover.png")));
        Assert.Equal(
            Path.Combine(userRoot, "images", "photo.png"),
            paths.TryResolveContentPath(Path.Combine("images", "photo.png")));
        Assert.Null(paths.TryResolveContentPath(
            Path.Combine("link-previews", "..", "..", "escape.png")));
    }

    [Fact]
    public void ContentResolverFallsBackToLegacyPreviewCache()
    {
        var userRoot = Path.Combine(_home, "legacy-preview-user-data");
        var localRoot = Path.Combine(_home, "legacy-preview-local-data");
        var legacyPreview = Path.Combine(
            userRoot,
            "link-previews",
            "cover.png");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPreview)!);
        File.WriteAllText(legacyPreview, "legacy");
        var paths = SentoryDataPaths.ForRoot(userRoot, localRoot);

        Assert.Equal(
            legacyPreview,
            paths.TryResolveContentPath(
                Path.Combine("link-previews", "cover.png")));
    }

    [Fact]
    public void RelativeEnvironmentOverrideIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            SentoryDataPaths.FromEnvironmentOrCurrentUser("relative-path"));
    }
}
