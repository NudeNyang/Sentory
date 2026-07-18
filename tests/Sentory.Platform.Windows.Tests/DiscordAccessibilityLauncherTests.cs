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
}
