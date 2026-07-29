using System.Threading.Channels;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public enum WhatsAppNativeDropRegistrationResult
{
    Registered,
    Paused,
    TargetInvalid,
    VisualBaselineUnavailable,
    UnsupportedFiles,
    ImageReadFailed,
    Duplicate,
    Failed
}

public sealed class WhatsAppCaptureRuntime : ICaptureRuntime
{
    private const int MaximumActiveCandidates = 8;

    private readonly INativeWindowApi _native;
    private readonly WhatsAppContextValidator _validator;
    private readonly LowLevelPasteHook _keyboardHook;
    private readonly LowLevelMouseHook _mouseHook;
    private readonly StaClipboardReader _clipboardReader;
    private readonly IWhatsAppVisualConfirmationClient _visual;
    private readonly CaptureCoordinator _coordinator;
    private readonly Action<string, string>? _diagnostic;
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
    private readonly List<CandidateRegistration> _candidates = [];
    private readonly WhatsAppRecentSendSignals _recentSendSignals = new();
    private Task? _worker;
    private volatile bool _paused;
    private DateTimeOffset _lastIssueReportedAt = DateTimeOffset.MinValue;

    public WhatsAppCaptureRuntime(
        ICaptureRepository repository,
        bool acceptInjectedInput = false,
        Action<string, string>? diagnostic = null)
    {
        var native = new NativeWindowApi();
        _native = native;
        _validator = new WhatsAppContextValidator(native);
        _keyboardHook = new LowLevelPasteHook(native, acceptInjectedInput);
        _mouseHook = new LowLevelMouseHook(native, acceptInjectedInput);
        _clipboardReader = new StaClipboardReader(native);
        _visual = new WhatsAppVisualConfirmationClient(
            new WhatsAppScreenFrameSource(native),
            diagnostic);
        _coordinator = new CaptureCoordinator(repository);
        _diagnostic = diagnostic;
    }

    public event EventHandler<CaptureNotification>? Captured;

    public event EventHandler<CaptureRuntimeIssue>? IssueDetected;

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

        _keyboardHook.PasteDetected += OnPasteDetected;
        _keyboardHook.SendDetected += OnSendDetected;
        _mouseHook.LeftButtonDown += OnPointerDown;
        _worker = Task.Run(() => ProcessTriggersAsync(_cancellation.Token));
        _keyboardHook.Start();
        _mouseHook.Start();
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
            _recentSendSignals.Observe(
                context.ContextHash,
                context.OccurredAt);
            _recentSendSignals.ObserveProcess(
                context.ProcessId,
                context.OccurredAt);
        }

        var observed = MarkSendObserved(
            context.MainWindow,
            context.OccurredAt,
            "keyboard");
        ReportBufferedSend("keyboard", observed);
    }

    private void OnPointerDown(object? sender, PointerTrigger trigger)
    {
        ExplorerPointerDownOriginTracker.ObserveShared(_native, trigger);
        if (_paused || trigger.ForegroundProcessId == 0 ||
            !string.Equals(
                _native.GetProcessName(trigger.ForegroundProcessId),
                WhatsAppContextValidator.ProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var root = _native.GetRootWindow(trigger.ForegroundWindow);
        if (root == nint.Zero ||
            _native.GetProcessId(root) != trigger.ForegroundProcessId ||
            !string.Equals(
                _native.GetClassName(root),
                WhatsAppContextValidator.MainWindowClassName,
                StringComparison.Ordinal))
        {
            return;
        }

        var bounds = _native.GetWindowBounds(root);
        if (!WhatsAppSendButtonPolicy.IsWithin(
                bounds,
                trigger.ScreenX,
                trigger.ScreenY))
        {
            return;
        }

        var focused = _native.GetFocusedWindow(root);
        var hasContext = _validator.TryValidate(
            new PasteTrigger(
                trigger.EventId,
                root,
                focused,
                trigger.ForegroundProcessId,
                _native.GetClipboardSequenceNumber(),
                trigger.OccurredAt,
                trigger.Injected),
            out var context);
        lock (_candidateGate)
        {
            if (hasContext)
            {
                _recentSendSignals.Observe(
                    context.ContextHash,
                    trigger.OccurredAt);
            }

            _recentSendSignals.ObserveProcess(
                trigger.ForegroundProcessId,
                trigger.OccurredAt);
        }

        var observed = MarkSendObserved(
            root,
            trigger.OccurredAt,
            hasContext ? "pointer" : "pointer-fallback");
        ReportBufferedSend(
            hasContext ? "pointer" : "pointer-fallback",
            observed);
    }

    private int MarkSendObserved(
        nint mainWindow,
        DateTimeOffset occurredAt,
        string inputKind)
    {
        var observed = 0;
        lock (_candidateGate)
        {
            var candidate = MessengerSendCandidatePolicy.SelectLatestEligible(
                _candidates,
                candidate =>
                    candidate.Context.MainWindow == mainWindow &&
                    candidate.Context.OccurredAt <= occurredAt &&
                    !candidate.IsSendObserved() &&
                    !candidate.Cancellation.IsCancellationRequested,
                candidate => candidate.Context.OccurredAt);
            if (candidate is not null &&
                _recentSendSignals.TryConsume(occurredAt) &&
                candidate.MarkSendObserved())
            {
                observed = 1;
            }
        }

        if (observed > 0)
        {
            _diagnostic?.Invoke(
                "whatsapp-send-input-observed",
                $"kind={inputKind} candidates={observed}");
        }

        return observed;
    }

    private void ReportBufferedSend(string inputKind, int observed)
    {
        if (observed == 0)
        {
            _diagnostic?.Invoke(
                "whatsapp-send-input-buffered",
                $"kind={inputKind} candidates=0");
        }
    }

    public async Task<WhatsAppNativeDropRegistrationResult>
        RegisterNativeDroppedFilesAsync(
            WhatsAppDropTarget target,
            IReadOnlyList<string> paths)
    {
        if (_paused)
        {
            return WhatsAppNativeDropRegistrationResult.Paused;
        }

        var occurredAt = DateTimeOffset.UtcNow;
        if (!_validator.TryValidate(
                target,
                _native.GetClipboardSequenceNumber(),
                occurredAt,
                out var context))
        {
            return WhatsAppNativeDropRegistrationResult.TargetInvalid;
        }

        var imagePaths = paths
            .Where(ClipboardImageCodec.IsSupportedImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (imagePaths.Length == 0)
        {
            return WhatsAppNativeDropRegistrationResult.UnsupportedFiles;
        }

        try
        {
            var baseline = await _visual.TryCaptureAsync(
                context,
                requireForeground: false,
                _cancellation.Token);
            if (baseline is null)
            {
                return WhatsAppNativeDropRegistrationResult
                    .VisualBaselineUnavailable;
            }

            var images = await Task.Run(
                () => ClipboardImageCodec.TryReadFiles(imagePaths),
                _cancellation.Token);
            if (images.Count == 0)
            {
                return WhatsAppNativeDropRegistrationResult.ImageReadFailed;
            }

            if (_paused)
            {
                return WhatsAppNativeDropRegistrationResult.Paused;
            }

            var registered = StartCandidate(
                context,
                baseline,
                null,
                [],
                images,
                nativeDrop: true);
            _diagnostic?.Invoke(
                "whatsapp-drop-candidate",
                $"registered={registered} files={imagePaths.Length} images={images.Count}");
            return registered
                ? WhatsAppNativeDropRegistrationResult.Registered
                : WhatsAppNativeDropRegistrationResult.Duplicate;
        }
        catch (OperationCanceledException)
            when (_cancellation.IsCancellationRequested)
        {
            return WhatsAppNativeDropRegistrationResult.Paused;
        }
        catch (Exception exception)
        {
            ReportIssue(exception);
            return WhatsAppNativeDropRegistrationResult.Failed;
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
        if (_paused || !_validator.TryValidate(trigger, out var context))
        {
            return;
        }

        var baseline = await _visual.TryCaptureAsync(
            context,
            requireForeground: true,
            cancellationToken);
        if (baseline is null)
        {
            return;
        }

        var clipboard = await _clipboardReader.ReadAsync(
            context.ClipboardSequenceNumber,
            cancellationToken);
        if (clipboard is null)
        {
            return;
        }

        var urls = UrlExtractor.Extract(clipboard.Text ?? string.Empty)
            .DistinctBy(url => url.Value, StringComparer.Ordinal)
            .ToList();
        var images = clipboard.Images
            .DistinctBy(
                image => image.Sha256,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (urls.Count == 0 && images.Count == 0)
        {
            return;
        }

        var registered = StartCandidate(
            context,
            baseline,
            clipboard.Text,
            urls,
            images,
            nativeDrop: false);
        _diagnostic?.Invoke(
            "whatsapp-paste-candidate",
            $"registered={registered} urls={urls.Count} images={images.Count}");
    }

    private bool StartCandidate(
        ValidatedWhatsAppContext context,
        WhatsAppVisualSnapshot baseline,
        string? clipboardText,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images,
        bool nativeDrop)
    {
        lock (_candidateGate)
        {
            _candidates.RemoveAll(candidate => candidate.Task.IsCompleted);
            var candidateUrls = urls
                .DistinctBy(url => url.Value, StringComparer.Ordinal)
                .ToList();
            var candidateImages = images
                .DistinctBy(
                    image => image.Sha256,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
            var payloadSignature =
                MessengerCandidateDuplicatePolicy.CreatePayloadSignature(
                    candidateUrls,
                    candidateImages);
            if ((candidateUrls.Count == 0 && candidateImages.Count == 0) ||
                _candidates.Count >= MaximumActiveCandidates ||
                _candidates.Any(candidate =>
                    !candidate.Cancellation.IsCancellationRequested &&
                    MessengerCandidateDuplicatePolicy.IsDuplicateBurst(
                        candidate.Context.ContextHash,
                        candidate.Context.OccurredAt,
                        candidate.PayloadSignature,
                        context.ContextHash,
                        context.OccurredAt,
                        payloadSignature)))
            {
                return false;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellation.Token);
            var registration = new CandidateRegistration(
                context,
                baseline,
                clipboardText,
                candidateUrls,
                candidateImages,
                nativeDrop,
                payloadSignature,
                cancellation);
            _candidates.Add(registration);
            if (_recentSendSignals.TryTakeApplicable(
                    context.ContextHash,
                    context.ProcessId,
                    context.OccurredAt,
                    DateTimeOffset.UtcNow))
            {
                registration.MarkSendObserved();
                _diagnostic?.Invoke(
                    "whatsapp-send-input-replayed",
                    "kind=keyboard candidates=1");
            }

            registration.Task = Task.Run(() => RunCandidateAsync(registration));
            return true;
        }
    }

    private async Task RunCandidateAsync(CandidateRegistration registration)
    {
        try
        {
            var response = await _visual.WaitForConfirmationAsync(
                new WhatsAppVisualConfirmationRequest(
                    registration.Context,
                    registration.Baseline,
                    TimeSpan.FromMinutes(2)),
                registration.IsSendObserved,
                registration.Cancellation.Token);
            if (!response.Confirmed || _paused)
            {
                return;
            }

            var signals = new List<string>(response.Signals)
            {
                registration.NativeDrop
                    ? "native-explorer-file-drop"
                    : "ctrl-v"
            };
            if (registration.IsSendObserved())
            {
                signals.Add("whatsapp-explicit-send-input");
            }

            var capturedAt = response.ConfirmedAt ?? DateTimeOffset.UtcNow;
            var result = await _coordinator.CaptureBatchAsync(
                registration.Context.EventId,
                registration.ClipboardText,
                registration.Images.Select(image => new ImageCapturePayload(
                    image.ContentBytes,
                    image.Sha256,
                    image.PixelWidth,
                    image.PixelHeight,
                    image.MimeType,
                    image.FileExtension,
                    image.OriginalFileName)).ToList(),
                SourceApp.WhatsApp,
                registration.NativeDrop
                    ? CaptureMethod.WhatsAppConfirmedDrop
                    : registration.Images.Count > 0
                        ? CaptureMethod.WhatsAppConfirmedImage
                        : CaptureMethod.WhatsAppConfirmedSend,
                DeliveryStatus.Confirmed,
                registration.Context.ContextHash,
                capturedAt,
                signals,
                registration.Cancellation.Token);
            if (result?.EventApplied != true)
            {
                return;
            }

            var memberCount = registration.Urls.Count +
                              registration.Images.Count;
            Captured?.Invoke(
                this,
                new CaptureNotification(
                    memberCount > 1
                        ? ContentKind.Collection
                        : registration.Images.Count > 0
                            ? ContentKind.Image
                            : ContentKind.Url,
                    1,
                    capturedAt,
                    SourceApp.WhatsApp,
                    DeliveryStatus.Confirmed));
            _diagnostic?.Invoke(
                "whatsapp-capture-applied",
                $"urls={registration.Urls.Count} images={registration.Images.Count} drop={registration.NativeDrop}");
        }
        catch (OperationCanceledException)
            when (registration.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReportIssue(exception);
        }
        finally
        {
            lock (_candidateGate)
            {
                _candidates.Remove(registration);
            }

            registration.Cancellation.Dispose();
        }
    }

    private void CancelActiveCandidates()
    {
        CandidateRegistration[] candidates;
        lock (_candidateGate)
        {
            candidates = _candidates.ToArray();
        }

        foreach (var candidate in candidates)
        {
            candidate.Cancellation.Cancel();
        }
    }

    private void ReportIssue(Exception exception)
    {
        _diagnostic?.Invoke(
            "whatsapp-capture-failed",
            $"type={exception.GetType().Name}");
        var now = DateTimeOffset.UtcNow;
        if (now - _lastIssueReportedAt < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastIssueReportedAt = now;
        IssueDetected?.Invoke(
            this,
            new CaptureRuntimeIssue(
                "whatsapp-capture-item-failed",
                "WhatsApp 입력 일부를 처리하지 못했지만 감지는 계속됩니다.",
                now));
    }

    public async ValueTask DisposeAsync()
    {
        _keyboardHook.PasteDetected -= OnPasteDetected;
        _keyboardHook.SendDetected -= OnSendDetected;
        _mouseHook.LeftButtonDown -= OnPointerDown;
        _keyboardHook.Dispose();
        _mouseHook.Dispose();
        _triggers.Writer.TryComplete();
        _cancellation.Cancel();
        CancelActiveCandidates();

        Task[] tasks;
        lock (_candidateGate)
        {
            tasks = _candidates.Select(candidate => candidate.Task).ToArray();
        }

        if (_worker is not null)
        {
            tasks = tasks.Append(_worker).ToArray();
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }

        _clipboardReader.Dispose();
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class CandidateRegistration(
        ValidatedWhatsAppContext context,
        WhatsAppVisualSnapshot baseline,
        string? clipboardText,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images,
        bool nativeDrop,
        string payloadSignature,
        CancellationTokenSource cancellation)
    {
        private int _sendObserved;

        public ValidatedWhatsAppContext Context { get; } = context;
        public WhatsAppVisualSnapshot Baseline { get; } = baseline;
        public string? ClipboardText { get; } = clipboardText;
        public IReadOnlyList<NormalizedUrl> Urls { get; } = urls;
        public IReadOnlyList<ClipboardImageSnapshot> Images { get; } = images;
        public bool NativeDrop { get; } = nativeDrop;
        public string PayloadSignature { get; } = payloadSignature;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = Task.CompletedTask;

        public bool MarkSendObserved() =>
            Interlocked.Exchange(ref _sendObserved, 1) == 0;

        public bool IsSendObserved() =>
            Volatile.Read(ref _sendObserved) == 1;
    }
}
