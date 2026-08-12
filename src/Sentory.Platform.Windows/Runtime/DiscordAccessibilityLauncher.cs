using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace Sentory.Platform.Windows.Runtime;

public enum DiscordAccessibilityArgumentState
{
    Unknown,
    Enabled,
    Missing
}

internal readonly record struct DiscordProcessCandidate(
    int ProcessId,
    bool HasMainWindow,
    DateTimeOffset StartedAt);

public sealed class DiscordAccessibilityLauncher
{
    private const string DiscordProcessName = "Discord";
    private readonly string _localAppData;
    private readonly Func<int, string?> _commandLineReader;

    public DiscordAccessibilityLauncher()
        : this(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal DiscordAccessibilityLauncher(string localAppData)
        : this(localAppData, ReadProcessCommandLine)
    {
    }

    internal DiscordAccessibilityLauncher(
        string localAppData,
        Func<int, string?> commandLineReader)
    {
        _localAppData = localAppData;
        _commandLineReader = commandLineReader;
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

    public DiscordAccessibilityArgumentState
        GetAccessibilityArgumentState(int processId)
    {
        try
        {
            return ClassifyAccessibilityArgument(
                _commandLineReader(processId));
        }
        catch (Exception exception)
            when (exception is ManagementException or
                  COMException or
                  InvalidOperationException or
                  UnauthorizedAccessException)
        {
            return DiscordAccessibilityArgumentState.Unknown;
        }
    }

    public bool IsPrivatePipeManaged(int processId)
    {
        try
        {
            var commandLine = _commandLineReader(processId);
            return string.IsNullOrWhiteSpace(commandLine) ||
                   ClassifyPrivatePipeManagement(commandLine);
        }
        catch (Exception exception)
            when (exception is ManagementException or
                  COMException or
                  InvalidOperationException or
                  UnauthorizedAccessException)
        {
            return true;
        }
    }

    internal static DiscordAccessibilityArgumentState
        ClassifyAccessibilityArgument(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return DiscordAccessibilityArgumentState.Unknown;
        }

        return ContainsCommandLineArgument(
            commandLine,
            "--force-renderer-accessibility")
                ? DiscordAccessibilityArgumentState.Enabled
                : DiscordAccessibilityArgumentState.Missing;
    }

    internal static bool ClassifyPrivatePipeManagement(string? commandLine) =>
        !string.IsNullOrWhiteSpace(commandLine) &&
        ContainsCommandLineArgument(commandLine, "--remote-debugging-pipe");

    public async Task<bool> WaitForMainProcessExitAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            timeout,
            TimeSpan.Zero);

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return true;
            }

            using var timeoutCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeoutCancellation.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(
                    timeoutCancellation.Token);
                return true;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

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

    public async Task<bool> RestartAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(LauncherPath))
        {
            throw new FileNotFoundException(
                "Discord 실행 파일을 찾지 못했습니다.",
                LauncherPath);
        }

        var mainProcessId = GetMainProcessId();
        if (mainProcessId is { } processId &&
            IsPrivatePipeManaged(processId))
        {
            return false;
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

        return true;
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

    private static string? ReadProcessCommandLine(int processId)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT CommandLine FROM Win32_Process " +
            $"WHERE ProcessId = {processId}");
        using var results = searcher.Get();
        foreach (ManagementObject process in results)
        {
            using (process)
            {
                return process["CommandLine"] as string;
            }
        }

        return null;
    }

    private static bool ContainsCommandLineArgument(
        string commandLine,
        string expectedArgument)
    {
        var searchFrom = 0;
        while (searchFrom < commandLine.Length)
        {
            var index = commandLine.IndexOf(
                expectedArgument,
                searchFrom,
                StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var beforeIsBoundary =
                index == 0 ||
                IsArgumentBoundary(commandLine[index - 1]);
            var afterIndex = index + expectedArgument.Length;
            var afterIsBoundary =
                afterIndex == commandLine.Length ||
                IsArgumentBoundary(commandLine[afterIndex]);
            if (beforeIsBoundary && afterIsBoundary)
            {
                return true;
            }

            searchFrom = index + expectedArgument.Length;
        }

        return false;
    }

    private static bool IsArgumentBoundary(char value) =>
        char.IsWhiteSpace(value) || value == '"';
}
