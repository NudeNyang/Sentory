using System.Diagnostics;
using System.IO;

namespace Sentory.Platform.Windows.Runtime;

internal readonly record struct DiscordProcessCandidate(
    int ProcessId,
    bool HasMainWindow,
    DateTimeOffset StartedAt);

public sealed class DiscordAccessibilityLauncher
{
    private const string DiscordProcessName = "Discord";
    private readonly string _localAppData;

    public DiscordAccessibilityLauncher()
        : this(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal DiscordAccessibilityLauncher(string localAppData)
    {
        _localAppData = localAppData;
    }

    internal string LauncherPath => Path.Combine(
        _localAppData,
        "Discord",
        "Update.exe");

    public bool IsInstalled => File.Exists(LauncherPath);

    internal ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = LauncherPath,
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("--processStart");
        startInfo.ArgumentList.Add("Discord.exe");
        startInfo.ArgumentList.Add("--process-start-args");
        startInfo.ArgumentList.Add("--force-renderer-accessibility");
        return startInfo;
    }

    public bool IsRunning()
    {
        var processes = Process.GetProcessesByName(DiscordProcessName);
        try
        {
            return processes.Any(IsRunning);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public int? GetMainProcessId()
    {
        var processes = Process.GetProcessesByName(DiscordProcessName);
        try
        {
            var candidates = new List<DiscordProcessCandidate>(
                processes.Length);
            foreach (var process in processes)
            {
                try
                {
                    candidates.Add(new DiscordProcessCandidate(
                        process.Id,
                        IsRunning(process) &&
                        process.MainWindowHandle != nint.Zero,
                        process.StartTime));
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }

            return SelectMainProcessId(candidates);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    internal static int? SelectMainProcessId(
        IEnumerable<DiscordProcessCandidate> candidates) =>
        candidates
            .Where(candidate => candidate.HasMainWindow)
            .OrderByDescending(candidate => candidate.StartedAt)
            .Select(candidate => (int?)candidate.ProcessId)
            .FirstOrDefault();

    public void Start()
    {
        if (!File.Exists(LauncherPath))
        {
            throw new FileNotFoundException(
                "Discord 실행 파일을 찾지 못했습니다.",
                LauncherPath);
        }

        if (IsRunning())
        {
            return;
        }

        if (Process.Start(CreateStartInfo()) is null)
        {
            throw new InvalidOperationException(
                "Discord를 시작하지 못했습니다.");
        }
    }

    public async Task RestartAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(LauncherPath))
        {
            throw new FileNotFoundException(
                "Discord 실행 파일을 찾지 못했습니다.",
                LauncherPath);
        }

        var processes = Process.GetProcessesByName(DiscordProcessName);
        try
        {
            foreach (var process in processes)
            {
                TryCloseMainWindow(process);
            }

            var gracefulDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < gracefulDeadline &&
                   processes.Any(IsRunning))
            {
                await Task.Delay(150, cancellationToken);
            }

            foreach (var process in processes.Where(IsRunning))
            {
                TryKill(process);
            }

            var exitDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < exitDeadline &&
                   processes.Any(IsRunning))
            {
                await Task.Delay(150, cancellationToken);
            }

            if (processes.Any(IsRunning))
            {
                throw new InvalidOperationException(
                    "Discord를 완전히 종료하지 못했습니다.");
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Process.Start(CreateStartInfo()) is null)
        {
            throw new InvalidOperationException(
                "Discord를 다시 시작하지 못했습니다.");
        }
    }

    private static bool IsRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryCloseMainWindow(Process process)
    {
        try
        {
            if (!process.HasExited && process.MainWindowHandle != nint.Zero)
            {
                process.CloseMainWindow();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
