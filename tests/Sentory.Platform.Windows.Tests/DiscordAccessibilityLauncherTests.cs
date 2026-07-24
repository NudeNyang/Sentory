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

    [Theory]
    [InlineData(
        "\"C:\\Discord\\Discord.exe\" --force-renderer-accessibility",
        DiscordAccessibilityArgumentState.Enabled)]
    [InlineData(
        "\"C:\\Discord\\Discord.exe\" \"--force-renderer-accessibility\"",
        DiscordAccessibilityArgumentState.Enabled)]
    [InlineData(
        "\"C:\\Discord\\Discord.exe\"",
        DiscordAccessibilityArgumentState.Missing)]
    [InlineData(
        "\"C:\\Discord\\Discord.exe\" --force-renderer-accessibility=false",
        DiscordAccessibilityArgumentState.Missing)]
    [InlineData(null, DiscordAccessibilityArgumentState.Unknown)]
    [InlineData("", DiscordAccessibilityArgumentState.Unknown)]
    public void ClassifiesAccessibilityArgumentFromTheCurrentProcess(
        string? commandLine,
        DiscordAccessibilityArgumentState expected)
    {
        Assert.Equal(
            expected,
            DiscordAccessibilityLauncher
                .ClassifyAccessibilityArgument(commandLine));
    }

    [Fact]
    public void ReturnsUnknownWhenTheCommandLineCannotBeRead()
    {
        var launcher = new DiscordAccessibilityLauncher(
            @"C:\Users\tester\AppData\Local",
            _ => throw new UnauthorizedAccessException());

        Assert.Equal(
            DiscordAccessibilityArgumentState.Unknown,
            launcher.GetAccessibilityArgumentState(42));
    }

    [Fact]
    public async Task ProcessExitWaitUsesATimeoutWithoutPolling()
    {
        var launcher = new DiscordAccessibilityLauncher(
            @"C:\Users\tester\AppData\Local");

        var exited = await launcher.WaitForMainProcessExitAsync(
            Environment.ProcessId,
            TimeSpan.FromMilliseconds(25),
            CancellationToken.None);

        Assert.False(exited);
    }
}
