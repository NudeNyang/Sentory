using System.Diagnostics;
using System.Text.Json;

namespace Sentory.Platform.Windows.Runtime;

public sealed class DiscordWorkerClient : IDiscordConfirmationClient
{
    public const string WorkerArgument = "--discord-accessibility-worker";

    private readonly Func<string?> _processPath;

    public DiscordWorkerClient()
        : this(() => Environment.ProcessPath)
    {
    }

    internal DiscordWorkerClient(Func<string?> processPath)
    {
        _processPath = processPath;
    }

    public async Task<DiscordConfirmationResponse> ConfirmAsync(
        DiscordConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        var executablePath = _processPath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return DiscordConfirmationResponse.Unavailable(
                "worker-executable-path-unavailable");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add(WorkerArgument);

        try
        {
            if (!process.Start())
            {
                return DiscordConfirmationResponse.Unavailable(
                    "worker-process-start-failed");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(
                cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(
                cancellationToken);
            await process.StandardInput.WriteLineAsync(
                JsonSerializer.Serialize(request));
            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(
                Math.Clamp(request.TimeoutMilliseconds, 1_000, 300_000) +
                10_000));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return DiscordConfirmationResponse.Unavailable(
                    "worker-process-timeout");
            }

            var output = await outputTask;
            _ = await errorTask;
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return DiscordConfirmationResponse.Unavailable(
                    $"worker-output-unavailable:exit-{process.ExitCode}");
            }

            return JsonSerializer.Deserialize<DiscordConfirmationResponse>(
                       output.Trim()) ??
                   DiscordConfirmationResponse.Unavailable();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (Exception)
        {
            TryKill(process);
            return DiscordConfirmationResponse.Unavailable(
                "worker-client-exception");
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
