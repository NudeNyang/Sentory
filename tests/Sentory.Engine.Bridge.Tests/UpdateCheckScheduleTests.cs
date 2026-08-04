namespace Sentory.Engine.Bridge.Tests;

using Sentory.Infrastructure.Updates;

public sealed class UpdateCheckScheduleTests
{
    [Fact]
    public void FirstAutomaticCheckRunsImmediately()
    {
        Assert.True(UpdateCheckSchedule.ShouldCheck(
            lastCheckedAt: null,
            now: DateTimeOffset.Parse("2026-08-05T00:00:00Z"),
            manual: false));
    }

    [Fact]
    public void AutomaticCheckWaitsSixHours()
    {
        var lastCheckedAt = DateTimeOffset.Parse("2026-08-05T00:00:00Z");

        Assert.False(UpdateCheckSchedule.ShouldCheck(
            lastCheckedAt,
            lastCheckedAt.AddHours(5).AddMinutes(59),
            manual: false));
        Assert.True(UpdateCheckSchedule.ShouldCheck(
            lastCheckedAt,
            lastCheckedAt.AddHours(6),
            manual: false));
    }

    [Fact]
    public void ManualCheckIgnoresAutomaticInterval()
    {
        var now = DateTimeOffset.Parse("2026-08-05T00:00:00Z");

        Assert.True(UpdateCheckSchedule.ShouldCheck(
            now,
            now,
            manual: true));
    }

    [Fact]
    public void InstalledBuildSelectsInstallerPackage()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Equal(
                UpdatePackageKind.Portable,
                UpdatePackageKindDetector.Resolve(directory));

            File.WriteAllText(Path.Combine(directory, "unins000.exe"), "fixture");

            Assert.Equal(
                UpdatePackageKind.Installer,
                UpdatePackageKindDetector.Resolve(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
