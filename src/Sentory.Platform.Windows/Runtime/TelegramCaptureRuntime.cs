using System.Threading.Channels;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public enum TelegramNativeDropRegistrationResult
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

public sealed class TelegramCaptureRuntime : ICaptureRuntime
{
    private const int MaximumActiveCandidates = 8;

    private readonly INativeWindowApi _native;
    private readonly TelegramContextValidator _validator;
    private readonly LowLevelPasteHook _keyboardHook;
    private readonly LowLevelMouseHook _mouseHook;
    private readonly StaClipboardReader _clipboardReader;
    private readonly ITelegramVisualConfirmationClient _visual;
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
    private readonly TelegramRecentSendSignals _recentSendSignals = new();
    private Task? _worker;
    private volatile bool _paused;
    private DateTimeOffset _lastIssueReportedAt = DateTimeOffset.MinValue;

    public TelegramCaptureRuntime(
        ICaptureRepository repository,
        bool acceptInjectedInput = false,
        Action<string, string>? diagnostic = null)
    {
        var native = new NativeWindowApi();
        _native = native;
        _validator = new TelegramContextValidator(native);
        _keyboardHook = new LowLevelPasteHook(native, acceptInjectedInput);
        _mouseHook = new LowLevelMouseHook(native, acceptInjectedInput);
        _clipboardReader = new StaClipboardReader(native);
        _visual = new TelegramVisualConfirmationClient(
            new TelegramScreenFrameSource(native),
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
        if (_paused || trigger.ForegroundProcessId == 0 ||
            !string.Equals(
                _native.GetProcessName(trigger.ForegroundProcessId),
                TelegramContextValidator.ProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_validator.TryValidate(trigger, out var context))
        {
            lock (_candidateGate)
            {
                _recentSendSignals.Observe(
                    context.ContextHash,
                    context.OccurredAt);
                _recentSendSignals.ObserveProcess(
                    context.ProcessId,
                    context.OccurredAt);
            }

            var exactObserved = MarkSendObserved(
                context.MainWindow,
                context.OccurredAt,
                "keyboard");
            if (exactObserved == 0)
            {
                _diagnostic?.Invoke(
                    "telegram-send-input-buffered",
                    "kind=keyboard candidates=0");
            }

            return;
        }

        lock (_candidateGate)
        {
            _recentSendSignals.ObserveProcess(
                trigger.ForegroundProcessId,
                trigger.OccurredAt);
        }

        var fallbackObserved = MarkSendObservedByProcess(
            trigger.ForegroundProcessId,
            trigger.OccurredAt,
            "keyboard-fallback");
        if (fallbackObserved == 0)
        {
            _diagnostic?.Invoke(
                "telegram-send-input-buffered",
                "kind=keyboard-fallback candidates=0");
        }
    }

    private void OnPointerDown(object? sender, PointerTrigger trigger)
    {
        if (_paused || trigger.ForegroundProcessId == 0 ||
            !string.Equals(
                _native.GetProcessName(trigger.ForegroundProcessId),
                TelegramContextValidator.ProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var hasContext = _validator.TryValidate(
                new PasteTrigger(
                    trigger.EventId,
                    trigger.ForegroundWindow,
                    trigger.ForegroundWindow,
                    trigger.ForegroundProcessId,
                    _native.GetClipboardSequenceNumber(),
                    trigger.OccurredAt,
                    trigger.Injected),
                out var context);
        var root = hasContext
            ? context.MainWindow
            : _native.GetRootWindow(trigger.ForegroundWindow);
        if (root == nint.Zero)
        {
            return;
        }

        var foregroundRoot = _native.GetRootWindow(
            trigger.ForegroundWindow);

        if (!TelegramSendButtonPolicy.IsWithin(
                _native.GetWindowBounds(
                    foregroundRoot != nint.Zero
                        ? foregroundRoot
                        : root),
                trigger.ScreenX,
                trigger.ScreenY))
        {
            return;
        }

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

        var observed = hasContext
            ? MarkSendObserved(
                root,
                trigger.OccurredAt,
                "pointer")
            : MarkSendObservedByProcess(
                trigger.ForegroundProcessId,
                trigger.OccurredAt,
                "pointer-fallback");
        if (observed == 0)
        {
            _diagnostic?.Invoke(
                "telegram-send-input-buffered",
                $"kind={(hasContext ? "pointer" : "pointer-fallback")} candidates=0");
        }
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
            if (candidate?.MarkSendObserved() == true)
            {
                observed = 1;
            }
        }

        if (observed > 0)
        {
            _diagnostic?.Invoke(
                "telegram-send-input-observed",
                $"kind={inputKind} candidates={observed}");
        }

        return observed;
    }

    private int MarkSendObservedByProcess(
        uint processId,
        DateTimeOffset occurredAt,
        string inputKind)
    {
        var observed = 0;
        lock (_candidateGate)
        {
            var candidate = MessengerSendCandidatePolicy.SelectLatestEligible(
                _candidates,
                candidate =>
                    candidate.Context.ProcessId == processId &&
                    candidate.Context.OccurredAt <= occurredAt &&
                    !candidate.IsSendObserved() &&
                    !candidate.Cancellation.IsCancellationRequested,
                candidate => candidate.Context.OccurredAt);
            if (candidate?.MarkSendObserved() == true)
            {
                observed = 1;
            }
        }

        if (observed > 0)
        {
            _diagnostic?.Invoke(
                "telegram-send-input-observed",
                $"kind={inputKind} candidates={observed}");
        }

        return observed;
    }

    public async Task<TelegramNativeDropRegistrationResult>
        RegisterNativeDroppedFilesAsync(
            TelegramDropTarget target,
            IReadOnlyList<string> paths,
            DateTimeOffset occurredAt) =>
        await RegisterNativeDroppedFilesAsync(
            target,
            paths,
            occurredAt,
            preDropSnapshot: null);

    internal async Task<TelegramNativeDropRegistrationResult>
        RegisterNativeDroppedFilesAsync(
            TelegramDropTarget target,
            IReadOnlyList<string> paths,
            DateTimeOffset occurredAt,
            TelegramVisualSnapshot? preDropSnapshot)
    {
        if (_paused)
        {
            return TelegramNativeDropRegistrationResult.Paused;
        }

        if (!_validator.TryValidate(
                target,
                _native.GetClipboardSequenceNumber(),
                occurredAt,
                out var context))
        {
            return TelegramNativeDropRegistrationResult.TargetInvalid;
        }

        var imagePaths = paths
            .Where(ClipboardImageCodec.IsSupportedImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (imagePaths.Length == 0)
        {
            return TelegramNativeDropRegistrationResult.UnsupportedFiles;
        }

        try
        {
            var baseline = await _visual.TryCaptureAsync(
                context,
                requireForeground: false,
                _cancellation.Token);
            if (baseline is null)
            {
                return TelegramNativeDropRegistrationResult
                    .VisualBaselineUnavailable;
            }

            var images = await Task.Run(
                () => ClipboardImageCodec.TryReadFiles(imagePaths),
                _cancellation.Token);
            if (images.Count == 0)
            {
                return TelegramNativeDropRegistrationResult.ImageReadFailed;
            }

            var registered = StartCandidate(
                context,
                baseline,
                null,
                [],
                images,
                nativeDrop: true,
                preDropSnapshot: preDropSnapshot);
            _diagnostic?.Invoke(
                "telegram-drop-candidate",
                $"registered={registered} files={imagePaths.Length} images={images.Count} confirmation=explicit-send-or-pre-drop-visual-change preDropBaseline={preDropSnapshot is not null}");
            return registered
                ? TelegramNativeDropRegistrationResult.Registered
                : TelegramNativeDropRegistrationResult.Duplicate;
        }
        catch (OperationCanceledException)
            when (_cancellation.IsCancellationRequested)
        {
            return TelegramNativeDropRegistrationResult.Paused;
        }
        catch (Exception exception)
        {
            ReportIssue(exception);
            return TelegramNativeDropRegistrationResult.Failed;
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

    internal async Task<TelegramVisualSnapshot?>
        TryCaptureNativeDropBaselineAsync(
            TelegramDropTarget target,
            DateTimeOffset occurredAt)
    {
        if (_paused || !_validator.TryValidate(
                target,
                _native.GetClipboardSequenceNumber(),
                occurredAt,
                out var context))
        {
            return null;
        }

        try
        {
            return await _visual.TryCaptureAsync(
                context,
                requireForeground: false,
                _cancellation.Token);
        }
        catch (OperationCanceledException)
            when (_cancellation.IsCancellationRequested)
        {
            return null;
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
            "telegram-paste-candidate",
            $"registered={registered} urls={urls.Count} images={images.Count}");
    }

    private bool StartCandidate(
        ValidatedTelegramContext context,
        TelegramVisualSnapshot baseline,
        string? clipboardText,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images,
        bool nativeDrop,
        TelegramVisualSnapshot? preDropSnapshot = null)
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
                preDropSnapshot,
                payloadSignature,
                cancellation);
            _candidates.Add(registration);
            if (_recentSendSignals.CanApply(
                    context.ContextHash,
                    context.ProcessId,
                    context.OccurredAt,
                    DateTimeOffset.UtcNow))
            {
                registration.MarkSendObserved();
                _diagnostic?.Invoke(
                    "telegram-send-input-replayed",
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
                new TelegramVisualConfirmationRequest(
                    registration.Context,
                    registration.Baseline,
                    TimeSpan.FromMinutes(2),
                    registration.PreDropSnapshot),
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
                SourceApp.Telegram,
                registration.NativeDrop
                    ? CaptureMethod.TelegramConfirmedDrop
                    : registration.Images.Count > 0
                        ? CaptureMethod.TelegramConfirmedImage
                        : CaptureMethod.TelegramConfirmedSend,
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
                    SourceApp.Telegram,
                    DeliveryStatus.Confirmed));
            _diagnostic?.Invoke(
                "telegram-capture-applied",
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
            "telegram-capture-failed",
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
                "telegram-capture-item-failed",
                "Telegram 입력 일부를 처리하지 못했지만 감지는 계속됩니다.",
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
        ValidatedTelegramContext context,
        TelegramVisualSnapshot baseline,
        string? clipboardText,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images,
        bool nativeDrop,
        TelegramVisualSnapshot? preDropSnapshot,
        string payloadSignature,
        CancellationTokenSource cancellation)
    {
        private int _sendObserved;

        public ValidatedTelegramContext Context { get; } = context;
        public TelegramVisualSnapshot Baseline { get; } = baseline;
        public string? ClipboardText { get; } = clipboardText;
        public IReadOnlyList<NormalizedUrl> Urls { get; } = urls;
        public IReadOnlyList<ClipboardImageSnapshot> Images { get; } = images;
        public bool NativeDrop { get; } = nativeDrop;
        public TelegramVisualSnapshot? PreDropSnapshot { get; } =
            preDropSnapshot;
        public string PayloadSignature { get; } = payloadSignature;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = Task.CompletedTask;

        public bool MarkSendObserved() =>
            Interlocked.Exchange(ref _sendObserved, 1) == 0;

        public bool IsSendObserved() =>
            Volatile.Read(ref _sendObserved) == 1;
    }
}
