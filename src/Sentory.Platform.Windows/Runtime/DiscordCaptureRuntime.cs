using System.Threading.Channels;
using System.Diagnostics;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public enum DiscordNativeDropRegistrationResult
{
    Registered,
    Paused,
    TargetInvalid,
    UnsupportedFiles,
    ImageReadFailed,
    Duplicate,
    Failed
}

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
    private readonly DiscordAttachmentDownloader _attachmentDownloader;
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
    private readonly List<AttachmentDiscoveryRegistration>
        _attachmentDiscoveries = [];
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
        _attachmentDownloader = new DiscordAttachmentDownloader();
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
            if (_paused == value)
            {
                return;
            }

            _paused = value;
            if (value)
            {
                // Cancellation can synchronously invoke callbacks owned by the
                // accessibility worker. Keep that work off the WPF input thread
                // so the tray pause action always responds immediately.
                CancelActiveCandidatesInBackground();
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

    public async Task<DiscordNativeDropRegistrationResult>
        RegisterNativeDroppedFilesAsync(
            DiscordDropTarget target,
            IReadOnlyList<string> paths)
    {
        if (_paused)
        {
            return DiscordNativeDropRegistrationResult.Paused;
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var trigger = new PasteTrigger(
            Guid.NewGuid(),
            target.MainWindow,
            target.RendererWindow,
            target.ProcessId,
            _native.GetClipboardSequenceNumber(),
            occurredAt,
            false);
        if (!_validator.TryValidate(trigger, out var context))
        {
            return DiscordNativeDropRegistrationResult.TargetInvalid;
        }

        var imagePaths = paths
            .Where(ClipboardImageCodec.IsSupportedImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (imagePaths.Length == 0)
        {
            return DiscordNativeDropRegistrationResult.UnsupportedFiles;
        }

        try
        {
            var images = await Task.Run(() => imagePaths
                .Select(ClipboardImageCodec.TryReadFile)
                .Where(image => image is not null)
                .Cast<ClipboardImageSnapshot>()
                .ToList());
            if (images.Count == 0)
            {
                return DiscordNativeDropRegistrationResult.ImageReadFailed;
            }

            if (_paused)
            {
                return DiscordNativeDropRegistrationResult.Paused;
            }

            var registered = StartCandidate(context, [], images);
            DiscordCaptureTrace.Write(
                "native-drop-candidate",
                $"registered={registered} files={imagePaths.Length} images={images.Count} bytes={images.Sum(image => image.ContentBytes.LongLength)}");
            return registered
                ? DiscordNativeDropRegistrationResult.Registered
                : DiscordNativeDropRegistrationResult.Duplicate;
        }
        catch (Exception exception)
            when (exception is System.IO.IOException or
                  UnauthorizedAccessException or
                  NotSupportedException)
        {
            DiscordCaptureTrace.Write(
                "native-drop-candidate-failed",
                $"type={exception.GetType().Name}");
            return DiscordNativeDropRegistrationResult.Failed;
        }
    }

    private void OnSendDetected(object? sender, PasteTrigger trigger)
    {
        if (_paused || !_validator.TryValidate(trigger, out var context))
        {
            return;
        }

        var hasClipboardImageCandidate = false;
        lock (_candidateGate)
        {
            _recentSendSignals[context.ContextHash] = context.OccurredAt;
            var candidates = _candidates.Where(candidate =>
                    string.Equals(
                        candidate.Context.ContextHash,
                        context.ContextHash,
                        StringComparison.Ordinal) &&
                    candidate.Context.OccurredAt <= context.OccurredAt)
                .ToList();
            var pendingCandidates = candidates
                .Where(candidate => !candidate.SendObserved.Task.IsCompleted)
                .ToList();
            AssignUrlSendBatch(pendingCandidates, context.OccurredAt);
            AssignImageSendBatch(pendingCandidates, context.OccurredAt);
            foreach (var candidate in pendingCandidates)
            {
                candidate.SendObserved.TrySetResult(context.OccurredAt);
            }

            hasClipboardImageCandidate = candidates.Any(candidate =>
                candidate.HasImages);
        }

        DiscordCaptureTrace.Write("discord-send-key-observed");
        if (!hasClipboardImageCandidate)
        {
            StartAttachmentDiscovery(context);
        }
    }

    private void StartAttachmentDiscovery(ValidatedDiscordContext context)
    {
        AttachmentDiscoveryRegistration registration;
        lock (_candidateGate)
        {
            _attachmentDiscoveries.RemoveAll(discovery =>
                discovery.Task.IsCompleted);
            if (_attachmentDiscoveries.Any(discovery =>
                    string.Equals(
                        discovery.Context.ContextHash,
                        context.ContextHash,
                        StringComparison.Ordinal)) ||
                _attachmentDiscoveries.Count >= 2)
            {
                return;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellation.Token);
            registration = new AttachmentDiscoveryRegistration(
                context,
                context.EventId,
                cancellation);
            _attachmentDiscoveries.Add(registration);
            registration.Task = RunAttachmentDiscoveryAsync(registration);
        }
    }

    private async Task RunAttachmentDiscoveryAsync(
        AttachmentDiscoveryRegistration registration)
    {
        try
        {
            var context = registration.Context;
            var response = await _confirmationClient.ConfirmAsync(
                new DiscordConfirmationRequest(
                    context.MainWindow.ToInt64(),
                    context.RendererWindow.ToInt64(),
                    context.ProcessId,
                    DiscordConfirmationContentKind.AttachmentDiscovery,
                    [],
                    15_000,
                    ExplicitSendObserved: true),
                registration.Cancellation.Token);
            var attachmentUrls = response.AttachmentUrls ?? [];
            DiscordCaptureTrace.Write(
                "attachment-discovery-response",
                $"outcome={response.Outcome} urls={attachmentUrls.Count} signals={string.Join(',', response.ConfirmationSignals)}");
            if (response.Outcome != DiscordConfirmationOutcome.Confirmed ||
                attachmentUrls.Count == 0 ||
                _paused)
            {
                return;
            }

            var images = await _attachmentDownloader.DownloadAsync(
                attachmentUrls,
                registration.Cancellation.Token);
            DiscordCaptureTrace.Write(
                "attachment-download-result",
                $"requested={attachmentUrls.Count} images={images.Count} bytes={images.Sum(image => image.ContentBytes.LongLength)}");
            if (images.Count == 0)
            {
                return;
            }

            var capturedAt = response.ConfirmedAt ?? DateTimeOffset.UtcNow;
            var signals = new List<string>(response.ConfirmationSignals)
            {
                "discord-send-key",
                "discord-sent-attachment-url",
                "discord-attachment-downloaded"
            };
            var result = await _coordinator.CaptureBatchAsync(
                registration.EventId,
                null,
                images.Select(image => new ImageCapturePayload(
                    image.ContentBytes,
                    image.Sha256,
                    image.PixelWidth,
                    image.PixelHeight,
                    image.MimeType,
                    image.FileExtension)).ToList(),
                SourceApp.Discord,
                CaptureMethod.DiscordConfirmedAttachment,
                DeliveryStatus.Confirmed,
                context.ContextHash,
                capturedAt,
                signals,
                registration.Cancellation.Token);
            if (result?.EventApplied == true)
            {
                Captured?.Invoke(
                    this,
                    new CaptureNotification(
                        images.Count > 1
                            ? ContentKind.Collection
                            : ContentKind.Image,
                        1,
                        capturedAt,
                        SourceApp.Discord,
                        DeliveryStatus.Confirmed));
            }
        }
        catch (OperationCanceledException)
            when (registration.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DiscordCaptureTrace.Write(
                "attachment-discovery-failed",
                $"type={exception.GetType().Name}");
            ReportIssue(exception);
        }
        finally
        {
            lock (_candidateGate)
            {
                _attachmentDiscoveries.Remove(registration);
            }

            registration.Cancellation.Dispose();
        }
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

        var urls = UrlExtractor.Extract(clipboard.Text ?? string.Empty)
            .GroupBy(url => url.Value, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var images = clipboard.Images
            .GroupBy(image => image.Sha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (urls.Count == 0 && images.Count == 0)
        {
            return;
        }

        DiscordCaptureTrace.Write(
            "clipboard-batch-read",
            $"urls={urls.Count} images={images.Count} bytes={images.Sum(image => image.ContentBytes.LongLength)} elapsedMs={Stopwatch.GetElapsedTime(readStartedAt).TotalMilliseconds:F0}");
        StartCandidate(context, urls, images);
    }

    private bool StartCandidate(
        ValidatedDiscordContext context,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images)
    {
        CandidateRegistration registration;
        lock (_candidateGate)
        {
            _candidates.RemoveAll(candidate => candidate.Task.IsCompleted);
            var candidateUrls = urls
                .Where(url => !_activeUrls.Contains(url.Value))
                .ToList();
            var candidateImages = images
                .Where(image => !_activeImageHashes.Contains(image.Sha256))
                .ToList();
            if ((candidateUrls.Count == 0 && candidateImages.Count == 0) ||
                _candidates.Count >= MaximumActiveCandidates)
            {
                return false;
            }

            foreach (var url in candidateUrls)
            {
                _activeUrls.Add(url.Value);
            }

            foreach (var image in candidateImages)
            {
                _activeImageHashes.Add(image.Sha256);
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellation.Token);
            registration = new CandidateRegistration(
                context,
                context.EventId,
                candidateUrls,
                candidateImages,
                cancellation);
            registration.Task = Task.Run(() => RunCandidateAsync(registration));
            _candidates.Add(registration);
            ApplyRecentSendSignal(registration);
            DiscordCaptureTrace.Write(
                "batch-candidate-started",
                $"urls={candidateUrls.Count} images={candidateImages.Count} active={_candidates.Count}");
            return true;
        }
    }

    private async Task RunCandidateAsync(CandidateRegistration registration)
    {
        try
        {
            var sendTimeout = registration.HasImages
                ? TimeSpan.FromMinutes(2)
                : TimeSpan.FromMinutes(5);
            _ = await registration.SendObserved.Task.WaitAsync(
                sendTimeout,
                registration.Cancellation.Token);
            var urlSendBatch = registration.UrlSendBatch;
            if (urlSendBatch is not null &&
                !urlSendBatch.IsLeader(registration.EventId))
            {
                return;
            }

            var imageSendBatch = registration.ImageSendBatch;
            if (imageSendBatch is not null &&
                !imageSendBatch.IsLeader(registration.EventId))
            {
                return;
            }

            await Task.Delay(350, registration.Cancellation.Token);
            var confirmedUrls = imageSendBatch?.SnapshotUrls() ??
                                urlSendBatch?.SnapshotUrls() ??
                                registration.Urls;
            var confirmedImages = imageSendBatch?.SnapshotImages() ??
                                  registration.Images;
            var response = await ConfirmAsync(
                registration,
                confirmedUrls,
                explicitSendObserved: true,
                registration.Cancellation.Token);

            DiscordCaptureTrace.Write(
                !registration.HasImages
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
            await CaptureConfirmedBatchAsync(
                registration,
                confirmedUrls,
                confirmedImages,
                response,
                signals);
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
            if (registration.HasImages)
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

                foreach (var image in registration.Images)
                {
                    _activeImageHashes.Remove(image.Sha256);
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

        var exhaustedState = ResolveWarmupExhaustedState(
            lastUnavailableState);
        _statusTracker.Publish(exhaustedState);
        if (exhaustedState == CaptureRuntimeState.ReconnectRequired)
        {
            ReportDetectionUnavailable();
        }
        else
        {
            DiscordCaptureTrace.Write(
                "worker-warmup-deferred",
                $"state={exhaustedState}");
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

    internal static CaptureRuntimeState ResolveWarmupExhaustedState(
        CaptureRuntimeState lastState) =>
        lastState == CaptureRuntimeState.Connecting
            ? CaptureRuntimeState.ReconnectRequired
            : lastState;

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
        IReadOnlyList<NormalizedUrl> urls,
        bool explicitSendObserved,
        CancellationToken cancellationToken)
    {
        var context = registration.Context;
        return _confirmationClient.ConfirmAsync(
            new DiscordConfirmationRequest(
                context.MainWindow.ToInt64(),
                context.RendererWindow.ToInt64(),
                context.ProcessId,
                !registration.HasImages
                    ? DiscordConfirmationContentKind.Url
                    : DiscordConfirmationContentKind.Image,
                urls.Select(url => url.Value).ToList(),
                registration.HasImages ? 120_000 : 300_000,
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
                registration.HasImages))
        {
            if (registration.HasImages)
            {
                var batch = _candidates
                    .Select(candidate => candidate.ImageSendBatch)
                    .FirstOrDefault(candidateBatch =>
                        candidateBatch is not null &&
                        string.Equals(
                            candidateBatch.ContextHash,
                            registration.Context.ContextHash,
                            StringComparison.Ordinal) &&
                        candidateBatch.SentAt == sentAt);
                if (batch is null)
                {
                    batch = new DiscordImageSendBatch(
                        registration.EventId,
                        registration.Context.ContextHash,
                        sentAt,
                        registration.Urls,
                        registration.Images);
                }
                else
                {
                    batch.Add(registration.Urls, registration.Images);
                }

                registration.ImageSendBatch = batch;
            }
            else if (registration.Urls.Count > 0)
            {
                var batch = _candidates
                    .Select(candidate => candidate.UrlSendBatch)
                    .FirstOrDefault(candidateBatch =>
                        candidateBatch is not null &&
                        string.Equals(
                            candidateBatch.ContextHash,
                            registration.Context.ContextHash,
                            StringComparison.Ordinal) &&
                        candidateBatch.SentAt == sentAt);
                if (batch is null)
                {
                    batch = new DiscordUrlSendBatch(
                        registration.EventId,
                        registration.Context.ContextHash,
                        sentAt,
                        registration.Urls);
                }
                else
                {
                    batch.Add(registration.Urls);
                }

                registration.UrlSendBatch = batch;
            }

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

    private async Task CaptureConfirmedBatchAsync(
        CandidateRegistration registration,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images,
        DiscordConfirmationResponse response,
        IReadOnlyList<string> signals)
    {
        var context = registration.Context;
        var capturedAt = response.ConfirmedAt ?? DateTimeOffset.UtcNow;
        var result = await _coordinator.CaptureBatchAsync(
            registration.EventId,
            string.Join('\n', urls.Select(url => url.Original)),
            images.Select(image => new ImageCapturePayload(
                image.ContentBytes,
                image.Sha256,
                image.PixelWidth,
                image.PixelHeight,
                image.MimeType,
                image.FileExtension,
                image.OriginalFileName)).ToList(),
            SourceApp.Discord,
            registration.HasImages
                ? CaptureMethod.DiscordConfirmedImage
                : CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            context.ContextHash,
            capturedAt,
            signals,
            registration.Cancellation.Token);
        if (result?.EventApplied == true)
        {
            var memberCount = urls.Count + images.Count;
            Captured?.Invoke(
                this,
                new CaptureNotification(
                    memberCount > 1
                        ? ContentKind.Collection
                        : registration.HasImages
                            ? ContentKind.Image
                            : ContentKind.Url,
                    1,
                    capturedAt,
                    SourceApp.Discord,
                    DeliveryStatus.Confirmed));
        }
        DiscordCaptureTrace.Write(
            "batch-capture-result",
            $"eventApplied={result?.EventApplied == true} urls={urls.Count} images={images.Count}");
    }

    private static void AssignUrlSendBatch(
        IReadOnlyList<CandidateRegistration> candidates,
        DateTimeOffset sentAt)
    {
        var urlCandidates = candidates
            .Where(candidate =>
                !candidate.HasImages &&
                candidate.Urls.Count > 0 &&
                candidate.UrlSendBatch is null)
            .ToList();
        if (urlCandidates.Count == 0)
        {
            return;
        }

        var leader = urlCandidates[0];
        var batch = new DiscordUrlSendBatch(
            leader.EventId,
            leader.Context.ContextHash,
            sentAt,
            leader.Urls);
        leader.UrlSendBatch = batch;
        foreach (var candidate in urlCandidates.Skip(1))
        {
            batch.Add(candidate.Urls);
            candidate.UrlSendBatch = batch;
        }

        DiscordCaptureTrace.Write(
            "url-send-batch-assigned",
            $"candidates={urlCandidates.Count} urls={batch.SnapshotUrls().Count}");
    }

    private static void AssignImageSendBatch(
        IReadOnlyList<CandidateRegistration> candidates,
        DateTimeOffset sentAt)
    {
        var imageCandidates = candidates
            .Where(candidate =>
                candidate.HasImages &&
                candidate.ImageSendBatch is null)
            .ToList();
        if (imageCandidates.Count == 0)
        {
            return;
        }

        var leader = imageCandidates[0];
        var batch = new DiscordImageSendBatch(
            leader.EventId,
            leader.Context.ContextHash,
            sentAt,
            leader.Urls,
            leader.Images);
        leader.ImageSendBatch = batch;
        foreach (var candidate in imageCandidates.Skip(1))
        {
            batch.Add(candidate.Urls, candidate.Images);
            candidate.ImageSendBatch = batch;
        }

        DiscordCaptureTrace.Write(
            "image-send-batch-assigned",
            $"candidates={imageCandidates.Count} images={batch.SnapshotImages().Count}");
    }

    private void CancelActiveCandidates()
    {
        lock (_candidateGate)
        {
            foreach (var candidate in _candidates)
            {
                candidate.Cancellation.Cancel();
            }

            foreach (var discovery in _attachmentDiscoveries)
            {
                discovery.Cancellation.Cancel();
            }
        }
    }

    private void CancelActiveCandidatesInBackground()
    {
        CancellationTokenSource[] cancellations;
        lock (_candidateGate)
        {
            cancellations = _candidates
                .Select(candidate => candidate.Cancellation)
                .Concat(_attachmentDiscoveries.Select(
                    discovery => discovery.Cancellation))
                .ToArray();
        }

        _ = BackgroundCancellation.Request(cancellations);
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
        Task[] discoveries;
        lock (_candidateGate)
        {
            candidates = _candidates.Select(candidate => candidate.Task).ToArray();
            discoveries = _attachmentDiscoveries
                .Select(discovery => discovery.Task)
                .ToArray();
        }

        try
        {
            await Task.WhenAll(candidates);
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await Task.WhenAll(discoveries);
        }
        catch (OperationCanceledException)
        {
        }

        _clipboardReader.Dispose();
        _attachmentDownloader.Dispose();
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class CandidateRegistration(
        ValidatedDiscordContext context,
        Guid eventId,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images,
        CancellationTokenSource cancellation)
    {
        public ValidatedDiscordContext Context { get; } = context;

        public Guid EventId { get; } = eventId;

        public IReadOnlyList<NormalizedUrl> Urls { get; } = urls;

        public IReadOnlyList<ClipboardImageSnapshot> Images { get; } = images;

        public bool HasImages => Images.Count > 0;

        public DiscordUrlSendBatch? UrlSendBatch { get; set; }

        public DiscordImageSendBatch? ImageSendBatch { get; set; }

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } =
            System.Threading.Tasks.Task.CompletedTask;

        public TaskCompletionSource<DateTimeOffset> SendObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class AttachmentDiscoveryRegistration(
        ValidatedDiscordContext context,
        Guid eventId,
        CancellationTokenSource cancellation)
    {
        public ValidatedDiscordContext Context { get; } = context;

        public Guid EventId { get; } = eventId;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } =
            System.Threading.Tasks.Task.CompletedTask;
    }
}
