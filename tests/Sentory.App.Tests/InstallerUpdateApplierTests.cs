using System.Diagnostics;

namespace Sentory.App.Tests;

public sealed class InstallerUpdateApplierTests
{
    [Theory]
    [InlineData(new[] { "--launch-installer-update" }, true)]
    [InlineData(new[] { "--LAUNCH-INSTALLER-UPDATE" }, true)]
    [InlineData(new[] { "--apply-portable-update" }, false)]
    [InlineData(new string[0], false)]
    public void DetectsInstallerHelperCommand(string[] args, bool expected)
    {
        Assert.Equal(expected, InstallerUpdateApplier.IsLaunchCommand(args));
    }

    [Fact]
    public void InstallerProgressRemainsVisibleDuringUpdate()
    {
        var info = InstallerUpdateApplier.CreateInstallerStartInfo(
            @"C:\Temp\Sentory-setup.exe",
            @"C:\Temp\Sentory-update.log");

        Assert.True(info.UseShellExecute);
        Assert.Equal(ProcessWindowStyle.Normal, info.WindowStyle);
        Assert.Contains("/SILENT", info.ArgumentList);
        Assert.DoesNotContain("/VERYSILENT", info.ArgumentList);
        Assert.Contains("/SUPPRESSMSGBOXES", info.ArgumentList);
        Assert.Contains("/CLOSEAPPLICATIONS", info.ArgumentList);
        Assert.Contains("/NORESTART", info.ArgumentList);
        Assert.Contains("/SENTORYUPDATE=1", info.ArgumentList);
        Assert.Contains(
            @"/LOG=C:\Temp\Sentory-update.log",
            info.ArgumentList);
    }

    [Fact]
    public void HelperWaitsForCurrentAppBeforeLaunchingInstaller()
    {
        var info = InstallerUpdateApplier.CreateHelperStartInfo(
            @"C:\Temp\Sentory.Update.exe",
            31415,
            @"C:\Temp\Sentory-setup.exe",
            @"C:\Program Files\Sentory\Sentory.exe",
            @"C:\Data\Sentory\logs\sentory.log");

        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Equal(
            new[]
            {
                InstallerUpdateApplier.LaunchArgument,
                "--wait-pid",
                "31415",
                "--installer",
                @"C:\Temp\Sentory-setup.exe",
                "--restart",
                @"C:\Program Files\Sentory\Sentory.exe",
                "--log",
                @"C:\Data\Sentory\logs\sentory.log"
            },
            info.ArgumentList);
    }
}
