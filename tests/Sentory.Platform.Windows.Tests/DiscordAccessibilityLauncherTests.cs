using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordAccessibilityLauncherTests
{
    [Fact]
    public void BuildsSquirrelLauncherPathUnderLocalAppData()
    {
        var launcher = new DiscordAccessibilityLauncher(
            @"C:\Users\tester\AppData\Local");

        Assert.Equal(
            @"C:\Users\tester\AppData\Local\Discord\Update.exe",
            launcher.LauncherPath);
    }

    [Fact]
    public void BuildsAccessibilityLaunchArguments()
    {
        var launcher = new DiscordAccessibilityLauncher(
            @"C:\Users\tester\AppData\Local");

        var startInfo = launcher.CreateStartInfo();

        Assert.Equal(launcher.LauncherPath, startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(
            [
                "--processStart",
                "Discord.exe",
                "--process-start-args",
                "--force-renderer-accessibility"
            ],
            startInfo.ArgumentList);
    }

    [Fact]
    public void SelectsOnlyTheDiscordProcessWithAMainWindow()
    {
        var processId = DiscordAccessibilityLauncher.SelectMainProcessId(
            [
                new DiscordProcessCandidate(
                    31,
                    false,
                    DateTimeOffset.Parse("2026-07-24T08:00:00+09:00")),
                new DiscordProcessCandidate(
                    47,
                    true,
                    DateTimeOffset.Parse("2026-07-24T08:01:00+09:00")),
                new DiscordProcessCandidate(
                    52,
                    false,
                    DateTimeOffset.Parse("2026-07-24T08:02:00+09:00"))
            ]);

        Assert.Equal(47, processId);
    }

    [Fact]
    public void SelectsTheNewestMainProcessDuringDiscordRestart()
    {
        var processId = DiscordAccessibilityLauncher.SelectMainProcessId(
            [
                new DiscordProcessCandidate(
                    47,
                    true,
                    DateTimeOffset.Parse("2026-07-24T08:01:00+09:00")),
                new DiscordProcessCandidate(
                    61,
                    true,
                    DateTimeOffset.Parse("2026-07-24T08:03:00+09:00"))
            ]);

        Assert.Equal(61, processId);
    }
}
