using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace Sentory.App;

internal static class PortableUpdateApplier
{
    public const string ApplyArgument = "--apply-portable-update";

    public static bool IsApplyCommand(IReadOnlyList<string> args) =>
        args.Contains(ApplyArgument, StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        try
        {
            var processId = int.Parse(RequiredValue(args, "--wait-pid"));
            var source = Path.GetFullPath(RequiredValue(args, "--source"));
            var target = Path.GetFullPath(RequiredValue(args, "--target"));
            var sourceExecutable = Path.Combine(source, "Sentory.exe");
            if (!File.Exists(sourceExecutable) || !Directory.Exists(target))
            {
                return 2;
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            }
            catch (ArgumentException)
            {
            }

            Exception? lastError = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    CopyDirectory(source, target);
                    lastError = null;
                    break;
                }
                catch (IOException exception)
                {
                    lastError = exception;
                    await Task.Delay(250);
                }
                catch (UnauthorizedAccessException exception)
                {
                    lastError = exception;
                    break;
                }
            }

            if (lastError is not null) throw lastError;
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(target, "Sentory.exe"),
                UseShellExecute = true,
                WorkingDirectory = target
            });
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "Sentory-update-error.txt"),
                    exception.ToString());
            }
            catch
            {
            }
            return 1;
        }
    }

    public static string PrepareAndLaunch(string archivePath)
    {
        var updateRoot = Path.Combine(
            Path.GetTempPath(),
            "Sentory",
            "apply-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(updateRoot, "package");
        Directory.CreateDirectory(source);
        ZipFile.ExtractToDirectory(archivePath, source, overwriteFiles: true);
        if (!File.Exists(Path.Combine(source, "Sentory.exe")))
        {
            throw new InvalidDataException("The portable update does not contain Sentory.exe.");
        }

        var helper = Path.Combine(updateRoot, "Sentory.Update.exe");
        File.Copy(Environment.ProcessPath!, helper, overwrite: true);
        var info = new ProcessStartInfo
        {
            FileName = helper,
            UseShellExecute = false,
            WorkingDirectory = updateRoot,
            CreateNoWindow = true
        };
        info.ArgumentList.Add(ApplyArgument);
        info.ArgumentList.Add("--wait-pid");
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        info.ArgumentList.Add("--source");
        info.ArgumentList.Add(source);
        info.ArgumentList.Add("--target");
        info.ArgumentList.Add(AppContext.BaseDirectory);
        Process.Start(info);
        return updateRoot;
    }

    private static string RequiredValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }
        throw new ArgumentException($"Missing update argument: {name}");
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(
                     source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(
                target,
                Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(
                     source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(
                target,
                Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
