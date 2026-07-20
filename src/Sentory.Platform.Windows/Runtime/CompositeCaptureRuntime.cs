using Sentory.Core;

namespace Sentory.Platform.Windows.Runtime;

public sealed class CompositeCaptureRuntime :
    ICaptureRuntime,
    ICaptureRuntimeStatusSource,
    ICaptureRuntimeRecoveryController,
    ICaptureRuntimeSourceController
{
    private readonly IReadOnlyList<ICaptureRuntime> _runtimes;
    private readonly IReadOnlyDictionary<SourceApp, ICaptureRuntime>
        _runtimesBySource;
    private readonly Dictionary<SourceApp, bool> _sourceEnabled = [];
    private bool _paused;
    private bool _started;

    public CompositeCaptureRuntime(params ICaptureRuntime[] runtimes)
        : this(runtimes, new Dictionary<SourceApp, ICaptureRuntime>())
    {
    }

    public CompositeCaptureRuntime(
        params (SourceApp SourceApp, ICaptureRuntime Runtime)[] runtimes)
        : this(
            runtimes.Select(value => value.Runtime).ToArray(),
            runtimes.ToDictionary(
                value => value.SourceApp,
                value => value.Runtime))
    {
    }

    private CompositeCaptureRuntime(
        IReadOnlyList<ICaptureRuntime> runtimes,
        IReadOnlyDictionary<SourceApp, ICaptureRuntime> runtimesBySource)
    {
        _runtimes = runtimes;
        _runtimesBySource = runtimesBySource;
        foreach (var sourceApp in _runtimesBySource.Keys)
        {
            _sourceEnabled[sourceApp] = true;
        }

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
            ApplyPauseStates();
        }
    }

    public bool IsSourceEnabled(SourceApp sourceApp) =>
        !_sourceEnabled.TryGetValue(sourceApp, out var enabled) || enabled;

    public void SetSourceEnabled(SourceApp sourceApp, bool enabled)
    {
        if (!_runtimesBySource.ContainsKey(sourceApp))
        {
            return;
        }

        _sourceEnabled[sourceApp] = enabled;
        ApplyPauseStates();
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

    private void ApplyPauseStates()
    {
        foreach (var runtime in _runtimes)
        {
            var source = _runtimesBySource
                .FirstOrDefault(pair => ReferenceEquals(pair.Value, runtime));
            runtime.IsPaused = _paused ||
                               (source.Value is not null &&
                                !IsSourceEnabled(source.Key));
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
        var disposalTasks = new List<Task>(_runtimes.Count);
        foreach (var runtime in _runtimes)
        {
            runtime.Captured -= ForwardCaptured;
            runtime.IssueDetected -= ForwardIssue;
            if (runtime is ICaptureRuntimeStatusSource statusSource)
            {
                statusSource.StatusChanged -= ForwardStatus;
            }

            disposalTasks.Add(runtime.DisposeAsync().AsTask());
        }

        await Task.WhenAll(disposalTasks);

        GC.SuppressFinalize(this);
    }
}
