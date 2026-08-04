using System.Diagnostics;
using Sentory.Infrastructure.Updates;

namespace Sentory.Engine.Bridge.Tests;

public sealed class UpdateApplierTests
{
    [Theory]
    [InlineData(new[] { "--apply-sentory-update" }, true)]
    [InlineData(new[] { "--APPLY-SENTORY-UPDATE" }, true)]
    [InlineData(new[] { "serve" }, false)]
    [InlineData(new string[0], false)]
    public void DetectsUpdateHelperCommand(string[] args, bool expected)
    {
        Assert.Equal(expected, UpdateApplier.IsApplyCommand(args));
    }

    [Fact]
    public void InstallerRunsSilentlyButKeepsProgressVisible()
    {
        var info = UpdateApplier.CreateInstallerStartInfo(
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
        Assert.Contains(@"/LOG=C:\Temp\Sentory-update.log", info.ArgumentList);
    }

    [Theory]
    [InlineData(UpdatePackageKind.Installer, "installer")]
    [InlineData(UpdatePackageKind.Portable, "portable")]
    public void HelperWaitsForHostAndEngineBeforeApplyingUpdate(
        UpdatePackageKind packageKind,
        string expectedKind)
    {
        var info = UpdateApplier.CreateHelperStartInfo(
            @"C:\Temp\Sentory.Update.exe",
            hostProcessId: 31415,
            engineProcessId: 92653,
            packageKind,
            @"C:\Temp\Sentory-package",
            @"C:\Program Files\Sentory",
            @"C:\Program Files\Sentory\Sentory.exe");

        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.Equal(UpdateApplier.ApplyArgument, info.ArgumentList[0]);
        Assert.Equal("31415", ValueAfter(info, "--wait-host-pid"));
        Assert.Equal("92653", ValueAfter(info, "--wait-engine-pid"));
        Assert.Equal(expectedKind, ValueAfter(info, "--package-kind"));
        Assert.Equal(@"C:\Temp\Sentory-package", ValueAfter(info, "--package"));
        Assert.Equal(@"C:\Program Files\Sentory", ValueAfter(info, "--target"));
        Assert.Equal(
            @"C:\Program Files\Sentory\Sentory.exe",
            ValueAfter(info, "--restart"));
    }

    private static string ValueAfter(ProcessStartInfo info, string name)
    {
        var index = info.ArgumentList.IndexOf(name);
        Assert.InRange(index, 0, info.ArgumentList.Count - 2);
        return info.ArgumentList[index + 1];
    }
}
