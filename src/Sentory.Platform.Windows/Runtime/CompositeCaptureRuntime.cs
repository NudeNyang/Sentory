using Sentory.Core;

namespace Sentory.Platform.Windows.Runtime;

public sealed class CompositeCaptureRuntime :
    ICaptureRuntime,
    ICaptureRuntimeStatusSource,
    ICaptureRuntimeRecoveryController
{
    private readonly IReadOnlyList<ICaptureRuntime> _runtimes;
    private bool _paused;
    private bool _started;

    public CompositeCaptureRuntime(params ICaptureRuntime[] runtimes)
    {
        _runtimes = runtimes;
        foreach (var runtime in _runtimes)
        {
            runtime.Captured += ForwardCaptured;
            runtime.IssueDetected += ForwardIssue;
            if (runtime is ICaptureRuntimeStatusSource statusSource)
            {
                statusSource.StatusChanged += ForwardStatus;
            }
        }
    }

    public event EventHandler<CaptureNotification>? Captured;

    public event EventHandler<CaptureRuntimeIssue>? IssueDetected;

    public event EventHandler<CaptureRuntimeStatus>? StatusChanged;

    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            foreach (var runtime in _runtimes)
            {
                runtime.IsPaused = value;
            }
        }
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        foreach (var runtime in _runtimes)
        {
            runtime.Start();
        }

        _started = true;
    }

    public void RequestRecovery(SourceApp sourceApp)
    {
        foreach (var runtime in _runtimes)
        {
            if (runtime is ICaptureRuntimeRecoveryController controller)
            {
                controller.RequestRecovery(sourceApp);
            }
        }
    }

    private void ForwardCaptured(
        object? sender,
        CaptureNotification notification) =>
        Captured?.Invoke(this, notification);

    private void ForwardIssue(
        object? sender,
        CaptureRuntimeIssue issue) =>
        IssueDetected?.Invoke(this, issue);

    private void ForwardStatus(
        object? sender,
        CaptureRuntimeStatus status) =>
        StatusChanged?.Invoke(this, status);

    public async ValueTask DisposeAsync()
    {
        foreach (var runtime in _runtimes)
        {
            runtime.Captured -= ForwardCaptured;
            runtime.IssueDetected -= ForwardIssue;
            if (runtime is ICaptureRuntimeStatusSource statusSource)
            {
                statusSource.StatusChanged -= ForwardStatus;
            }
            await runtime.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
