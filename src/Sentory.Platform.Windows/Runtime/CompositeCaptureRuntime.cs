using Sentory.Core;

namespace Sentory.Platform.Windows.Runtime;

public sealed class CompositeCaptureRuntime : ICaptureRuntime
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
        }
    }

    public event EventHandler<CaptureNotification>? Captured;

    public event EventHandler<CaptureRuntimeIssue>? IssueDetected;

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

    private void ForwardCaptured(
        object? sender,
        CaptureNotification notification) =>
        Captured?.Invoke(this, notification);

    private void ForwardIssue(
        object? sender,
        CaptureRuntimeIssue issue) =>
        IssueDetected?.Invoke(this, issue);

    public async ValueTask DisposeAsync()
    {
        foreach (var runtime in _runtimes)
        {
            runtime.Captured -= ForwardCaptured;
            runtime.IssueDetected -= ForwardIssue;
            await runtime.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
