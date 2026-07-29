using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Sentory.Platform.Windows.Runtime;

public sealed class DiscordWorkerClient :
    IDiscordConfirmationClient,
    IDiscordWorkerLifecycle,
    IAsyncDisposable
{
    public const string WorkerArgument = "--discord-accessibility-worker";

    private readonly Func<string?> _processPath;
    private readonly object _processGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<
        Guid,
        TaskCompletionSource<DiscordConfirmationResponse>> _pending = [];
    private Process? _process;
    private bool _disposed;

    public DiscordWorkerClient()
        : this(() => Environment.ProcessPath)
    {
    }

    internal DiscordWorkerClient(Func<string?> processPath)
    {
        _processPath = processPath;
    }

    public event EventHandler? RecoveryRequired;

    public async Task<DiscordConfirmationResponse> ConfirmAsync(
        DiscordConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();
        var completion =
            new TaskCompletionSource<DiscordConfirmationResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
        {
            return DiscordConfirmationResponse.Unavailable(
                "worker-request-registration-failed");
        }

        try
        {
            await SendAsync(
                new DiscordWorkerMessage(
                    requestId,
                    DiscordWorkerOperation.Confirm,
                    request),
                cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(
                Math.Clamp(request.TimeoutMilliseconds, 1_000, 300_000) +
                10_000));
            try
            {
                return await completion.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                await TrySendCancellationAsync(requestId);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return DiscordConfirmationResponse.Unavailable(
                    "worker-request-timeout");
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            await TrySendCancellationAsync(requestId);
            throw;
        }
        catch (Exception exception)
        {
            return DiscordConfirmationResponse.Unavailable(
                $"worker-client:{exception.GetType().Name}");
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task SendAsync(
        DiscordWorkerMessage message,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var process = EnsureProcess();
            await process.StandardInput.WriteLineAsync(
                JsonSerializer.Serialize(message));
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task TrySendCancellationAsync(Guid requestId)
    {
        try
        {
            await SendAsync(
                new DiscordWorkerMessage(
                    requestId,
                    DiscordWorkerOperation.Cancel,
                    null),
                CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    private Process EnsureProcess()
    {
        lock (_processGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process is { } running && IsRunning(running))
            {
                return running;
            }

            DisposeProcess(_process);
            var executablePath = _processPath();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException(
                    "Worker executable path is unavailable.");
            }

            var process = new Process
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
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException(
                    "Worker process failed to start.");
            }

            _process = process;
            _ = ReadResponsesAsync(process);
            _ = DrainErrorsAsync(process);
            return process;
        }
    }

    private async Task ReadResponsesAsync(Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                var response =
                    JsonSerializer.Deserialize<DiscordWorkerResponse>(line);
                if (response is not null &&
                    _pending.TryRemove(
                        response.RequestId,
                        out var completion))
                {
                    completion.TrySetResult(response.Response);
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            HandleProcessExit(process);
        }
    }

    private static async Task DrainErrorsAsync(Process process)
    {
        try
        {
            _ = await process.StandardError.ReadToEndAsync();
        }
        catch (Exception)
        {
        }
    }

    private void HandleProcessExit(Process process)
    {
        var notifyRecovery = false;
        lock (_processGate)
        {
            if (!ReferenceEquals(_process, process))
            {
                return;
            }

            _process = null;
            DisposeProcess(process);
            notifyRecovery = !_disposed;
            foreach (var pair in _pending.ToArray())
            {
                if (_pending.TryRemove(pair.Key, out var completion))
                {
                    completion.TrySetResult(
                        DiscordConfirmationResponse.Unavailable(
                            "worker-process-exited"));
                }
            }
        }

        if (notifyRecovery)
        {
            RecoveryRequired?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool TryRecycle()
    {
        lock (_processGate)
        {
            if (_disposed)
            {
                return false;
            }

            var process = _process;
            _process = null;
            DisposeProcess(process);
            foreach (var pair in _pending.ToArray())
            {
                if (_pending.TryRemove(pair.Key, out var completion))
                {
                    completion.TrySetResult(
                        DiscordConfirmationResponse.Unavailable(
                            "worker-recycled"));
                }
            }

            return true;
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

    private static void DisposeProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            process.StandardInput.Close();
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
        finally
        {
            process.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_processGate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            var process = _process;
            _process = null;
            DisposeProcess(process);
            foreach (var pair in _pending.ToArray())
            {
                if (_pending.TryRemove(pair.Key, out var completion))
                {
                    completion.TrySetCanceled();
                }
            }
        }

        _writeGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
