using System.Diagnostics;
using System.IO;
using Sentory.Core.Diagnostics;

namespace Sentory.App;

internal static class InstallerUpdateApplier
{
    public const string LaunchArgument = "--launch-installer-update";

    public static bool IsLaunchCommand(IReadOnlyList<string> args) =>
        args.Contains(LaunchArgument, StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        string? restartPath = null;
        string? diagnosticsLogPath = null;
        try
        {
            var processId = int.Parse(RequiredValue(args, "--wait-pid"));
            var installerPath = Path.GetFullPath(
                RequiredValue(args, "--installer"));
            restartPath = Path.GetFullPath(
                RequiredValue(args, "--restart"));
            diagnosticsLogPath = Path.GetFullPath(
                RequiredValue(args, "--log"));
            if (!File.Exists(installerPath))
            {
                throw new FileNotFoundException(
                    "The downloaded update installer is missing.",
                    installerPath);
            }

            WriteLog(
                diagnosticsLogPath,
                "update-installer-helper-started",
                "Waiting for the current Sentory process to exit");
            await WaitForProcessExitAsync(processId);

            var installerLogPath = Path.Combine(
                AppContext.BaseDirectory,
                "Sentory-update-installer.log");
            WriteLog(
                diagnosticsLogPath,
                "update-installer-started",
                "Starting the downloaded update installer");
            using var installer = Process.Start(
                CreateInstallerStartInfo(
                    installerPath,
                    installerLogPath)) ?? throw new InvalidOperationException(
                        "The update installer did not start.");
            await installer.WaitForExitAsync();
            if (installer.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The update installer exited with code {installer.ExitCode}.");
            }

            WriteLog(
                diagnosticsLogPath,
                "update-installer-completed",
                "The update installer completed successfully");
            return 0;
        }
        catch (Exception exception)
        {
            WriteLog(
                diagnosticsLogPath,
                "update-installer-failed",
                "The update installer helper failed",
                exception);
            TryRestart(restartPath);
            return 1;
        }
    }

    public static string PrepareAndLaunch(
        string installerPath,
        string diagnosticsLogPath)
    {
        var fullInstallerPath = Path.GetFullPath(installerPath);
        if (!File.Exists(fullInstallerPath))
        {
            throw new FileNotFoundException(
                "The downloaded update installer is missing.",
                fullInstallerPath);
        }

        var currentExecutable = Environment.ProcessPath ??
            throw new InvalidOperationException(
                "The current Sentory executable path is unavailable.");
        var updateRoot = Path.Combine(
            Path.GetTempPath(),
            "Sentory",
            "installer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateRoot);
        var helperPath = Path.Combine(updateRoot, "Sentory.Update.exe");
        File.Copy(currentExecutable, helperPath, overwrite: true);
        _ = Process.Start(CreateHelperStartInfo(
            helperPath,
            Environment.ProcessId,
            fullInstallerPath,
            currentExecutable,
            Path.GetFullPath(diagnosticsLogPath))) ??
            throw new InvalidOperationException(
                "The update installer helper did not start.");
        return updateRoot;
    }

    internal static ProcessStartInfo CreateInstallerStartInfo(
        string installerPath,
        string installerLogPath)
    {
        var info = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath),
            WindowStyle = ProcessWindowStyle.Normal
        };
        info.ArgumentList.Add("/SILENT");
        info.ArgumentList.Add("/SUPPRESSMSGBOXES");
        info.ArgumentList.Add("/CLOSEAPPLICATIONS");
        info.ArgumentList.Add("/NORESTART");
        info.ArgumentList.Add("/SENTORYUPDATE=1");
        info.ArgumentList.Add($"/LOG={installerLogPath}");
        return info;
    }

    internal static ProcessStartInfo CreateHelperStartInfo(
        string helperPath,
        int processId,
        string installerPath,
        string restartPath,
        string diagnosticsLogPath)
    {
        var info = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(helperPath),
            CreateNoWindow = true
        };
        info.ArgumentList.Add(LaunchArgument);
        info.ArgumentList.Add("--wait-pid");
        info.ArgumentList.Add(processId.ToString());
        info.ArgumentList.Add("--installer");
        info.ArgumentList.Add(installerPath);
        info.ArgumentList.Add("--restart");
        info.ArgumentList.Add(restartPath);
        info.ArgumentList.Add("--log");
        info.ArgumentList.Add(diagnosticsLogPath);
        return info;
    }

    private static async Task WaitForProcessExitAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (ArgumentException)
        {
        }
    }

    private static string RequiredValue(
        IReadOnlyList<string> args,
        string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(
                    args[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        throw new ArgumentException($"Missing update argument: {name}");
    }

    private static void TryRestart(string? restartPath)
    {
        if (string.IsNullOrWhiteSpace(restartPath) ||
            !File.Exists(restartPath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = restartPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(restartPath)
            });
        }
        catch
        {
        }
    }

    private static void WriteLog(
        string? diagnosticsLogPath,
        string category,
        string message,
        Exception? exception = null)
    {
        if (string.IsNullOrWhiteSpace(diagnosticsLogPath))
        {
            return;
        }

        SentoryDiagnosticLogFile.Append(
            diagnosticsLogPath,
            category,
            message,
            exception);
    }
}
