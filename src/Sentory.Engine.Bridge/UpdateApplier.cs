using System.Diagnostics;
using System.IO.Compression;
using Sentory.Infrastructure.Updates;

namespace Sentory.Engine.Bridge;

internal static class UpdateApplier
{
    public const string ApplyArgument = "--apply-sentory-update";

    public static bool IsApplyCommand(IReadOnlyList<string> args) =>
        args.Contains(ApplyArgument, StringComparer.OrdinalIgnoreCase);

    public static string PrepareAndLaunch(
        string packagePath,
        UpdatePackageKind packageKind,
        int hostProcessId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostProcessId);
        var package = Path.GetFullPath(packagePath);
        if (!File.Exists(package))
        {
            throw new FileNotFoundException(
                "다운로드한 업데이트 패키지를 찾지 못했습니다.",
                package);
        }

        var target = Path.GetFullPath(AppContext.BaseDirectory);
        var restart = Path.Combine(target, "Sentory.exe");
        var updateRoot = Path.Combine(
            Path.GetTempPath(),
            "Sentory",
            "apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateRoot);

        var preparedPackage = package;
        if (packageKind == UpdatePackageKind.Portable)
        {
            preparedPackage = Path.Combine(updateRoot, "package");
            Directory.CreateDirectory(preparedPackage);
            ZipFile.ExtractToDirectory(
                package,
                preparedPackage,
                overwriteFiles: true);
            RequirePortableFile(preparedPackage, "Sentory.exe");
            RequirePortableFile(preparedPackage, "sentory-engine.exe");
        }

        var helper = Path.Combine(updateRoot, "Sentory.Update.exe");
        File.Copy(
            Environment.ProcessPath ?? throw new InvalidOperationException(
                "업데이트 헬퍼 원본을 찾지 못했습니다."),
            helper,
            overwrite: true);
        var info = CreateHelperStartInfo(
            helper,
            hostProcessId,
            Environment.ProcessId,
            packageKind,
            preparedPackage,
            target,
            restart);
        _ = Process.Start(info) ?? throw new InvalidOperationException(
            "업데이트 헬퍼를 시작하지 못했습니다.");
        return updateRoot;
    }

    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        string? restart = null;
        try
        {
            var hostProcessId = ReadPositiveInt(args, "--wait-host-pid");
            var engineProcessId = ReadPositiveInt(args, "--wait-engine-pid");
            var packageKind = ParsePackageKind(
                RequiredValue(args, "--package-kind"));
            var package = Path.GetFullPath(RequiredValue(args, "--package"));
            var target = Path.GetFullPath(RequiredValue(args, "--target"));
            restart = Path.GetFullPath(RequiredValue(args, "--restart"));

            await WaitForProcessExitAsync(hostProcessId);
            await WaitForProcessExitAsync(engineProcessId);

            if (packageKind == UpdatePackageKind.Installer)
            {
                if (!File.Exists(package))
                {
                    throw new FileNotFoundException(
                        "업데이트 설치 파일을 찾지 못했습니다.",
                        package);
                }

                var installerLog = Path.Combine(target, "Sentory-update-installer.log");
                using var installer = Process.Start(
                    CreateInstallerStartInfo(package, installerLog)) ??
                    throw new InvalidOperationException(
                        "업데이트 설치 프로그램을 시작하지 못했습니다.");
                await installer.WaitForExitAsync();
                if (installer.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"업데이트 설치 프로그램이 코드 {installer.ExitCode}로 종료됐습니다.");
                }
            }
            else
            {
                RequirePortableFile(package, "Sentory.exe");
                RequirePortableFile(package, "sentory-engine.exe");
                await CopyDirectoryWithRetryAsync(package, target);
                StartApplication(restart);
            }

            return 0;
        }
        catch (Exception exception)
        {
            TryWriteFailure(exception);
            TryRestart(restart);
            return 1;
        }
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
        int hostProcessId,
        int engineProcessId,
        UpdatePackageKind packageKind,
        string package,
        string target,
        string restart)
    {
        var info = new ProcessStartInfo
        {
            FileName = helperPath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(helperPath),
            CreateNoWindow = true
        };
        info.ArgumentList.Add(ApplyArgument);
        info.ArgumentList.Add("--wait-host-pid");
        info.ArgumentList.Add(hostProcessId.ToString());
        info.ArgumentList.Add("--wait-engine-pid");
        info.ArgumentList.Add(engineProcessId.ToString());
        info.ArgumentList.Add("--package-kind");
        info.ArgumentList.Add(
            packageKind == UpdatePackageKind.Installer ? "installer" : "portable");
        info.ArgumentList.Add("--package");
        info.ArgumentList.Add(package);
        info.ArgumentList.Add("--target");
        info.ArgumentList.Add(target);
        info.ArgumentList.Add("--restart");
        info.ArgumentList.Add(restart);
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

    private static async Task CopyDirectoryWithRetryAsync(
        string source,
        string target)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                CopyDirectory(source, target);
                return;
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

        throw lastError ?? new IOException("업데이트 파일을 교체하지 못했습니다.");
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(
                target,
                Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            var destination = Path.Combine(
                target,
                Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void RequirePortableFile(string directory, string fileName)
    {
        if (!File.Exists(Path.Combine(directory, fileName)))
        {
            throw new InvalidDataException(
                $"포터블 업데이트에 {fileName} 파일이 없습니다.");
        }
    }

    private static int ReadPositiveInt(IReadOnlyList<string> args, string name)
    {
        var value = int.Parse(RequiredValue(args, name));
        return value > 0
            ? value
            : throw new ArgumentOutOfRangeException(name);
    }

    private static UpdatePackageKind ParsePackageKind(string value) =>
        value.ToLowerInvariant() switch
        {
            "installer" => UpdatePackageKind.Installer,
            "portable" => UpdatePackageKind.Portable,
            _ => throw new ArgumentException("지원하지 않는 업데이트 패키지 형식입니다.")
        };

    private static string RequiredValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        throw new ArgumentException($"업데이트 인자가 없습니다: {name}");
    }

    private static void StartApplication(string path)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path)
        });
    }

    private static void TryRestart(string? restart)
    {
        if (string.IsNullOrWhiteSpace(restart) || !File.Exists(restart))
        {
            return;
        }

        try
        {
            StartApplication(restart);
        }
        catch
        {
        }
    }

    private static void TryWriteFailure(Exception exception)
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
    }
}
