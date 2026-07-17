using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Sentory.Diagnostics.Uia;

public static class PowerShellUiaProbe
{
    public static async Task<JsonElement> CaptureAsync(
        CliOptions options,
        TimeSpan timeout)
    {
        var systemDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.System);
        var powerShellPath = Path.Combine(
            systemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var scriptPath = Path.Combine(
            AppContext.BaseDirectory,
            "Uia",
            "Probe.ps1");

        if (!File.Exists(powerShellPath))
        {
            throw new CliException("Windows PowerShell 5.1을 찾을 수 없어.");
        }

        if (!File.Exists(scriptPath))
        {
            throw new CliException("UIA 프로브 스크립트를 찾을 수 없어.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = powerShellPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ProcessNames");
        startInfo.ArgumentList.Add(string.Join(',', options.ProcessNames));
        startInfo.ArgumentList.Add("-View");
        startInfo.ArgumentList.Add(options.View.ToString().ToLowerInvariant());
        startInfo.ArgumentList.Add("-MaxElements");
        startInfo.ArgumentList.Add(options.MaxElements.ToString());

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new CliException("UIA 프로브 프로세스를 시작하지 못했어.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);

        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await Task.WhenAll(standardOutput, standardError);
            throw new CliException(
                $"UIA 프로브가 {timeout.TotalSeconds:0}초 안에 응답하지 않아 결과를 폐기했어.");
        }

        var output = await standardOutput;
        _ = await standardError;

        if (process.ExitCode != 0)
        {
            throw new CliException(
                $"UIA 프로브가 종료 코드 {process.ExitCode}로 실패했어.");
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new CliException("UIA 프로브가 유효한 JSON을 반환하지 않았어.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between timeout and cleanup.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process already became unavailable.
        }
    }
}
