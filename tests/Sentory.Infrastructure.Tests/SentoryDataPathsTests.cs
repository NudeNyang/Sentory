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
            Path.Combine(paths.RootDirectory, "link-previews"),
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
}
