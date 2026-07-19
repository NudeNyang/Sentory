using System.Threading.Channels;
using System.Diagnostics;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public sealed class DiscordCaptureRuntime :
    ICaptureRuntime,
    ICaptureRuntimeStatusSource,
    ICaptureRuntimeRecoveryController
{
    private const int MaximumActiveCandidates = 8;

    private readonly INativeWindowApi _native;
    private readonly IDiscordWindowApi _discordWindows;
    private readonly DiscordContextValidator _validator;
    private readonly LowLevelPasteHook _hook;
    private readonly StaClipboardReader _clipboardReader;
    private readonly IDiscordConfirmationClient _confirmationClient;
    private readonly IDiscordWorkerLifecycle? _workerLifecycle;
    private readonly DiscordDetectionStatusTracker _statusTracker = new();
    private readonly CaptureCoordinator _coordinator;
    private readonly Channel<PasteTrigger> _triggers =
        Channel.CreateBounded<PasteTrigger>(
            new BoundedChannelOptions(32)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _candidateGate = new();
    private readonly object _warmupGate = new();
    private readonly List<CandidateRegistration> _candidates = [];
    private readonly HashSet<string> _activeUrls =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeImageHashes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _recentSendSignals =
        new(StringComparer.Ordinal);
    private Task? _worker;
    private Task? _warmupTask;
    private volatile bool _paused;
    private DateTimeOffset _lastIssueReportedAt = DateTimeOffset.MinValue;

    public DiscordCaptureRuntime(
        ICaptureRepository repository,
        bool acceptInjectedInput = false)
    {
        var native = new NativeWindowApi();
        _native = native;
        _discordWindows = native;
        _validator = new DiscordContextValidator(native, native);
        _hook = new LowLevelPasteHook(native, acceptInjectedInput);
        _clipboardReader = new StaClipboardReader(native);
        _confirmationClient = new DiscordWorkerClient();
        _workerLifecycle = _confirmationClient as IDiscordWorkerLifecycle;
        if (_workerLifecycle is not null)
        {
            _workerLifecycle.RecoveryRequired += OnWorkerRecoveryRequired;
        }
        _coordinator = new CaptureCoordinator(repository);
    }

    public event EventHandler<CaptureNotification>? Captured;

    public event EventHandler<CaptureRuntimeIssue>? IssueDetected;

    public event EventHandler<CaptureRuntimeStatus>? StatusChanged
    {
        add => _statusTracker.StatusChanged += value;
        remove => _statusTracker.StatusChanged -= value;
    }

    public bool IsPaused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (value)
            {
                CancelActiveCandidates();
            }
        }
    }

    public void Start()
    {
        if (_worker is not null)
        {
            return;
        }

        _hook.PasteDetected += OnPasteDetected;
        _hook.SendDetected += OnSendDetected;
        _worker = Task.Run(() => ProcessTriggersAsync(_cancellation.Token));
        _hook.Start();
        BeginWorkerWarmup(recovering: false);
    }

    public void RequestRecovery(SourceApp sourceApp)
    {
        if (sourceApp != SourceApp.Discord ||
            _cancellation.IsCancellationRequested)
        {
            return;
        }

        CancelActiveCandidates();
        _statusTracker.Publish(CaptureRuntimeState.Connecting);
        BeginWorkerWarmup(recovering: false);
    }

    private void OnPasteDetected(object? sender, PasteTrigger trigger) =>
        _triggers.Writer.TryWrite(trigger);

    private void OnSendDetected(object? sender, PasteTrigger trigger)
    {
        if (_paused || !_validator.TryValidate(trigger, out var context))
        {
            return;
        }

        lock (_candidateGate)
        {
            _recentSendSignals[context.ContextHash] = context.OccurredAt;
            foreach (var candidate in _candidates.Where(candidate =>
                         string.Equals(
                             candidate.Context.ContextHash,
                             context.ContextHash,
                             StringComparison.Ordinal) &&
                         candidate.Context.OccurredAt <= context.OccurredAt))
            {
                candidate.SendObserved.TrySetResult(context.OccurredAt);
            }
        }

        DiscordCaptureTrace.Write("discord-send-key-observed");
    }

    private async Task ProcessTriggersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ResilientWorkLoop.RunAsync(
                _triggers.Reader.ReadAllAsync(cancellationToken),
                ProcessTriggerAsync,
                ReportIssue,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessTriggerAsync(
        PasteTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (_paused ||
            !_validator.TryValidate(trigger, out var context))
        {
            return;
        }

        DiscordCaptureTrace.Write(
            "paste-context-validated",
            $"sequence={context.ClipboardSequenceNumber}");

        var readStartedAt = Stopwatch.GetTimestamp();
        var clipboard = await _clipboardReader.ReadAsync(
            context.ClipboardSequenceNumber,
            cancellationToken);
        if (clipboard is null)
        {
            DiscordCaptureTrace.Write("clipboard-read-empty");
            return;
        }

        if (clipboard.Images.Count > 0)
        {
            DiscordCaptureTrace.Write(
                "clipboard-image-read",
                $"count={clipboard.Images.Count} bytes={clipboard.Images.Sum(image => image.PngBytes.LongLength)} elapsedMs={Stopwatch.GetElapsedTime(readStartedAt).TotalMilliseconds:F0}");
            foreach (var image in clipboard.Images)
            {
                StartImageCandidate(context, image);
            }
            return;
        }

        if (clipboard.Text is null)
        {
            return;
        }

        var urls = UrlExtractor.Extract(clipboard.Text)
            .GroupBy(url => url.Value, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (urls.Count == 0)
        {
            return;
        }

        DiscordCaptureTrace.Write(
            "clipboard-url-read",
            $"count={urls.Count}");
        StartCandidate(context, urls);
    }

    private void StartCandidate(
        ValidatedDiscordContext context,
        IReadOnlyList<NormalizedUrl> urls)
    {
        CandidateRegistration registration;
        lock (_candidateGate)
        {
            _candidates.RemoveAll(candidate => candidate.Task.IsCompleted);
            var candidateUrls = urls
                .Where(url => !_activeUrls.Contains(url.Value))
                .ToList();
            if (candidateUrls.Count == 0 ||
                _candidates.Count >= MaximumActiveCandidates)
            {
                return;
            }

            foreach (var url in candidateUrls)
            {
                _activeUrls.Add(url.Value);
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellation.Token);
            registration = new CandidateRegistration(
                context,
                context.EventId,
                candidateUrls,
                null,
                cancellation);
            registration.Task = Task.Run(() => RunCandidateAsync(registration));
            _candidates.Add(registration);
            ApplyRecentSendSignal(registration);
            DiscordCaptureTrace.Write(
                "url-candidate-started",
                $"count={candidateUrls.Count} active={_candidates.Count}");
        }
    }

    private void StartImageCandidate(
        ValidatedDiscordContext context,
        ClipboardImageSnapshot image)
    {
        CandidateRegistration registration;
        lock (_candidateGate)
        {
            _candidates.RemoveAll(candidate => candidate.Task.IsCompleted);
            if (_activeImageHashes.Contains(image.Sha256) ||
                _candidates.Count >= MaximumActiveCandidates)
            {
                DiscordCaptureTrace.Write(
                    "image-candidate-skipped",
                    $"duplicate={_activeImageHashes.Contains(image.Sha256)} active={_candidates.Count}");
                return;
            }

            _activeImageHashes.Add(image.Sha256);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellation.Token);
            registration = new CandidateRegistration(
                context,
                CaptureBatchIdentity.ForImage(context.EventId, image.Sha256),
                [],
                image,
                cancellation);
            registration.Task = Task.Run(() => RunCandidateAsync(registration));
            _candidates.Add(registration);
            ApplyRecentSendSignal(registration);
            DiscordCaptureTrace.Write(
                "image-candidate-started",
                $"active={_candidates.Count}");
        }
    }

    private async Task RunCandidateAsync(CandidateRegistration registration)
    {
        try
        {
            var sendTimeout = registration.Image is null
                ? TimeSpan.FromMinutes(5)
                : TimeSpan.FromMinutes(2);
            _ = await registration.SendObserved.Task.WaitAsync(
                sendTimeout,
                registration.Cancellation.Token);
            await Task.Delay(350, registration.Cancellation.Token);
            var response = await ConfirmAsync(
                registration,
                explicitSendObserved: true,
                registration.Cancellation.Token);

            DiscordCaptureTrace.Write(
                registration.Image is null
                    ? "url-confirmation-response"
                    : "image-confirmation-response",
                $"outcome={response.Outcome} signals={string.Join(',', response.ConfirmationSignals)}");
            if (response.Outcome ==
                DiscordConfirmationOutcome.DetectionUnavailable)
            {
                var unavailableState =
                    ClassifyUnavailableState(response);
                _statusTracker.Publish(unavailableState);
                if (unavailableState == CaptureRuntimeState.Recovering)
                {
                    BeginWorkerWarmup(recovering: true);
                }
                else if (unavailableState ==
                         CaptureRuntimeState.ReconnectRequired)
                {
                    ReportDetectionUnavailable();
                }
                else
                {
                    DiscordCaptureTrace.Write(
                        "discord-target-waiting",
                        $"signals={string.Join(',', response.ConfirmationSignals)}");
                }
                return;
            }

            _statusTracker.Publish(CaptureRuntimeState.Ready);

            if (response.Outcome != DiscordConfirmationOutcome.Confirmed ||
                _paused)
            {
                return;
            }

            var signals = new List<string>(response.ConfirmationSignals.Count + 2)
            {
                "ctrl-v",
                "clipboard-sequence-stable"
            };
            signals.AddRange(response.ConfirmationSignals);
            if (registration.Image is { } image)
            {
                await CaptureConfirmedImageAsync(
                    registration,
                    image,
                    response,
                    signals);
            }
            else
            {
                await CaptureConfirmedUrlsAsync(
                    registration,
                    response,
                    signals);
            }
        }
        catch (OperationCanceledException)
            when (registration.Cancellation.IsCancellationRequested)
        {
        }
        catch (TimeoutException)
        {
        }
        catch (Exception exception)
        {
            if (registration.Image is not null)
            {
                DiscordCaptureTrace.Write(
                    "image-candidate-failed",
                    $"type={exception.GetType().Name}");
            }

            ReportIssue(exception);
        }
        finally
        {
            lock (_candidateGate)
            {
                foreach (var url in registration.Urls)
                {
                    _activeUrls.Remove(url.Value);
                }

                if (registration.Image is not null)
                {
                    _activeImageHashes.Remove(registration.Image.Sha256);
                }

                _candidates.Remove(registration);
            }

            registration.Cancellation.Dispose();
        }
    }

    private void BeginWorkerWarmup(bool recovering)
    {
        lock (_warmupGate)
        {
            if (_warmupTask is { IsCompleted: false })
            {
                return;
            }

            _warmupTask = WarmWorkerWithRetryAsync(
                recovering,
                _cancellation.Token);
        }
    }

    private async Task WarmWorkerWithRetryAsync(
        bool recovering,
        CancellationToken cancellationToken)
    {
        _statusTracker.Publish(
            recovering
                ? CaptureRuntimeState.Recovering
                : CaptureRuntimeState.Connecting);
        var lastUnavailableState = CaptureRuntimeState.Connecting;
        for (var attempt = 0; attempt < 15; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryCreateWarmupRequest(out var request))
            {
                var startedAt = DateTimeOffset.UtcNow;
                var response = await _confirmationClient.ConfirmAsync(
                    request,
                    cancellationToken);
                DiscordCaptureTrace.Write(
                    "worker-warmup-response",
                    $"outcome={response.Outcome} elapsedMs={(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:F0} signals={string.Join(',', response.ConfirmationSignals)}");
                if (response.Outcome == DiscordConfirmationOutcome.Confirmed)
                {
                    _statusTracker.Publish(CaptureRuntimeState.Ready);
                    return;
                }

                lastUnavailableState =
                    ClassifyUnavailableState(response);
            }
            else
            {
                lastUnavailableState = CaptureRuntimeState.Connecting;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        _statusTracker.Publish(lastUnavailableState);
        if (lastUnavailableState == CaptureRuntimeState.ReconnectRequired)
        {
            ReportDetectionUnavailable();
        }
        else
        {
            DiscordCaptureTrace.Write(
                "worker-warmup-deferred",
                $"state={lastUnavailableState}");
        }
    }

    private void OnWorkerRecoveryRequired(object? sender, EventArgs eventArgs)
    {
        if (_cancellation.IsCancellationRequested)
        {
            return;
        }

        DiscordCaptureTrace.Write("worker-recovery-started");
        _statusTracker.Publish(CaptureRuntimeState.Recovering);
        BeginWorkerWarmup(recovering: true);
    }

    internal static bool IsWorkerFailure(
        DiscordConfirmationResponse response) =>
        response.Outcome == DiscordConfirmationOutcome.DetectionUnavailable &&
        response.ConfirmationSignals.Any(signal =>
            signal.StartsWith("worker-", StringComparison.Ordinal));

    internal static CaptureRuntimeState ClassifyUnavailableState(
        DiscordConfirmationResponse response)
    {
        if (IsWorkerFailure(response))
        {
            return CaptureRuntimeState.Recovering;
        }

        return response.Outcome ==
                   DiscordConfirmationOutcome.DetectionUnavailable &&
               response.ConfirmationSignals.Contains(
                   "renderer-accessibility-root-unavailable",
                   StringComparer.Ordinal)
            ? CaptureRuntimeState.ReconnectRequired
            : CaptureRuntimeState.Connecting;
    }

    private bool TryCreateWarmupRequest(
        out DiscordConfirmationRequest request)
    {
        request = null!;
        var processes = Process.GetProcessesByName(
            DiscordContextValidator.DiscordProcessName);
        try
        {
            foreach (var process in processes)
            {
                nint mainWindow;
                try
                {
                    if (process.HasExited ||
                        (mainWindow = process.MainWindowHandle) == nint.Zero)
                    {
                        continue;
                    }
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (!string.Equals(
                        _native.GetClassName(mainWindow),
                        DiscordContextValidator.MainWindowClassName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var renderer = _discordWindows.FindDescendant(
                    mainWindow,
                    DiscordContextValidator.RendererClassName);
                var processId = _native.GetProcessId(mainWindow);
                if (renderer == nint.Zero ||
                    processId == 0 ||
                    _native.GetProcessId(renderer) != processId)
                {
                    continue;
                }

                request = new DiscordConfirmationRequest(
                    mainWindow.ToInt64(),
                    renderer.ToInt64(),
                    processId,
                    DiscordConfirmationContentKind.Warmup,
                    [],
                    30_000);
                return true;
            }

            return false;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private Task<DiscordConfirmationResponse> ConfirmAsync(
        CandidateRegistration registration,
        bool explicitSendObserved,
        CancellationToken cancellationToken)
    {
        var context = registration.Context;
        return _confirmationClient.ConfirmAsync(
            new DiscordConfirmationRequest(
                context.MainWindow.ToInt64(),
                context.RendererWindow.ToInt64(),
                context.ProcessId,
                registration.Image is null
                    ? DiscordConfirmationContentKind.Url
                    : DiscordConfirmationContentKind.Image,
                registration.Urls.Select(url => url.Value).ToList(),
                registration.Image is null ? 300_000 : 120_000,
                explicitSendObserved),
            cancellationToken);
    }

    private void ApplyRecentSendSignal(CandidateRegistration registration)
    {
        var now = DateTimeOffset.UtcNow;
        if (_recentSendSignals.TryGetValue(
                registration.Context.ContextHash,
                out var sentAt) &&
            DiscordSendSignalPolicy.CanAssociate(
                registration.Context.OccurredAt,
                sentAt,
                now,
                registration.Image is not null))
        {
            registration.SendObserved.TrySetResult(sentAt);
        }

        foreach (var expired in _recentSendSignals
                     .Where(pair =>
                         now - pair.Value > DiscordSendSignalPolicy.Retention)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _recentSendSignals.Remove(expired);
        }
    }

    private async Task CaptureConfirmedUrlsAsync(
        CandidateRegistration registration,
        DiscordConfirmationResponse response,
        IReadOnlyList<string> signals)
    {
        var context = registration.Context;
        var capturedAt = response.ConfirmedAt ?? DateTimeOffset.UtcNow;
        var results = await _coordinator.CaptureUrlsAsync(
            registration.EventId,
            string.Join('\n', registration.Urls.Select(url => url.Original)),
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            context.ContextHash,
            capturedAt,
            signals,
            registration.Cancellation.Token);
        var applied = results.Count(result => result.EventApplied);
        if (applied > 0)
        {
            Captured?.Invoke(
                this,
                new CaptureNotification(
                    ContentKind.Url,
                    applied,
                    capturedAt,
                    SourceApp.Discord,
                    DeliveryStatus.Confirmed));
        }
    }

    private async Task CaptureConfirmedImageAsync(
        CandidateRegistration registration,
        ClipboardImageSnapshot image,
        DiscordConfirmationResponse response,
        IReadOnlyList<string> signals)
    {
        var context = registration.Context;
        var capturedAt = response.ConfirmedAt ?? DateTimeOffset.UtcNow;
        var result = await _coordinator.CaptureImageAsync(
            registration.EventId,
            image.PngBytes,
            image.Sha256,
            image.PixelWidth,
            image.PixelHeight,
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedImage,
            DeliveryStatus.Confirmed,
            context.ContextHash,
            capturedAt,
            signals,
            registration.Cancellation.Token);
        DiscordCaptureTrace.Write(
            "image-capture-result",
            $"eventApplied={result.EventApplied}");
        if (result.EventApplied)
        {
            Captured?.Invoke(
                this,
                new CaptureNotification(
                    ContentKind.Image,
                    1,
                    capturedAt,
                    SourceApp.Discord,
                    DeliveryStatus.Confirmed));
        }
    }

    private void CancelActiveCandidates()
    {
        lock (_candidateGate)
        {
            foreach (var candidate in _candidates)
            {
                candidate.Cancellation.Cancel();
            }
        }
    }

    private void ReportDetectionUnavailable()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastIssueReportedAt < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _lastIssueReportedAt = now;
        IssueDetected?.Invoke(
            this,
            new CaptureRuntimeIssue(
                "discord-detection-unavailable",
                "Discord 입력 구조를 확인하지 못해 해당 링크나 사진을 저장하지 않았습니다.",
                now));
    }

    private void ReportIssue(Exception exception)
    {
        _ = exception;
        var now = DateTimeOffset.UtcNow;
        if (now - _lastIssueReportedAt < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastIssueReportedAt = now;
        IssueDetected?.Invoke(
            this,
            new CaptureRuntimeIssue(
                "discord-capture-item-failed",
                "일부 Discord 전송을 처리하지 못했지만 감지는 계속됩니다.",
                now));
    }

    public async ValueTask DisposeAsync()
    {
        if (_workerLifecycle is not null)
        {
            _workerLifecycle.RecoveryRequired -= OnWorkerRecoveryRequired;
        }
        _hook.PasteDetected -= OnPasteDetected;
        _hook.SendDetected -= OnSendDetected;
        _hook.Dispose();
        _triggers.Writer.TryComplete();
        _cancellation.Cancel();
        CancelActiveCandidates();
        if (_confirmationClient is IAsyncDisposable disposableClient)
        {
            await disposableClient.DisposeAsync();
        }

        if (_warmupTask is not null)
        {
            try
            {
                await _warmupTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (OperationCanceledException)
            {
            }
        }

        Task[] candidates;
        lock (_candidateGate)
        {
            candidates = _candidates.Select(candidate => candidate.Task).ToArray();
        }

        try
        {
            await Task.WhenAll(candidates);
        }
        catch (OperationCanceledException)
        {
        }

        _clipboardReader.Dispose();
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class CandidateRegistration(
        ValidatedDiscordContext context,
        Guid eventId,
        IReadOnlyList<NormalizedUrl> urls,
        ClipboardImageSnapshot? image,
        CancellationTokenSource cancellation)
    {
        public ValidatedDiscordContext Context { get; } = context;

        public Guid EventId { get; } = eventId;

        public IReadOnlyList<NormalizedUrl> Urls { get; } = urls;

        public ClipboardImageSnapshot? Image { get; } = image;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } =
            System.Threading.Tasks.Task.CompletedTask;

        public TaskCompletionSource<DateTimeOffset> SendObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
