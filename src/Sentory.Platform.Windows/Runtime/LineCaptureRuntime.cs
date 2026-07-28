using System.Threading.Channels;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public enum LineNativeDropRegistrationResult
{
    Registered,
    Paused,
    TargetInvalid,
    ConversationUnavailable,
    UnsupportedFiles,
    ImageReadFailed,
    Duplicate,
    Failed
}

public sealed class LineCaptureRuntime : ICaptureRuntime
{
    private const int MaximumActiveCandidates = 8;

    private readonly INativeWindowApi _native;
    private readonly LineContextValidator _validator;
    private readonly LowLevelPasteHook _keyboardHook;
    private readonly LowLevelMouseHook _mouseHook;
    private readonly StaClipboardReader _clipboardReader;
    private readonly ILineAccessibilityClient _accessibility;
    private readonly ILinePointerSendVerifier _pointerSendVerifier;
    private readonly ILineComposerTextReader _composerTextReader;
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
    private readonly HashSet<string> _activeUrls =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeImageHashes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LineRecentSendSignals _recentSendSignals = new();
    private Task? _worker;
    private volatile bool _paused;
    private DateTimeOffset _lastIssueReportedAt = DateTimeOffset.MinValue;

    public LineCaptureRuntime(
        ICaptureRepository repository,
        bool acceptInjectedInput = false,
        Action<string, string>? diagnostic = null)
    {
        var native = new NativeWindowApi();
        _native = native;
        _validator = new LineContextValidator(native);
        _keyboardHook = new LowLevelPasteHook(native, acceptInjectedInput);
        _mouseHook = new LowLevelMouseHook(native, acceptInjectedInput);
        _clipboardReader = new StaClipboardReader(native);
        _accessibility = new LineAccessibilityClient(diagnostic);
        _pointerSendVerifier = new LinePointerSendVerifier();
        _composerTextReader = new LineComposerTextReader();
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
                LineContextValidator.ProcessName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_validator.TryValidate(trigger, out var context))
        {
            var composer = _composerTextReader.Read(
                context.MainWindow,
                context.ProcessId);
            lock (_candidateGate)
            {
                _recentSendSignals.Observe(
                    context.ContextHash,
                    context.OccurredAt,
                    composer.IsAvailable ? composer.Text : null);
                _recentSendSignals.ObserveProcess(
                    context.ProcessId,
                    context.OccurredAt,
                    composer.IsAvailable ? composer.Text : null);
            }

            var exactObserved = MarkSendObserved(
                context.MainWindow,
                context.OccurredAt,
                "keyboard",
                composer);
            if (exactObserved == 0)
            {
                _diagnostic?.Invoke(
                    "line-send-input-buffered",
                    "kind=keyboard candidates=0");
            }

            return;
        }

        var fallbackRoot = _native.GetRootWindow(trigger.ForegroundWindow);
        var fallbackComposer = fallbackRoot == nint.Zero
            ? new LineComposerTextSnapshot(false, string.Empty)
            : _composerTextReader.Read(
                fallbackRoot,
                trigger.ForegroundProcessId);
        lock (_candidateGate)
        {
            _recentSendSignals.ObserveProcess(
                trigger.ForegroundProcessId,
                trigger.OccurredAt,
                fallbackComposer.IsAvailable
                    ? fallbackComposer.Text
                    : null);
        }

        var fallbackObserved = MarkSendObservedByProcess(
            trigger.ForegroundProcessId,
            trigger.OccurredAt,
            "keyboard-fallback",
            fallbackComposer);
        if (fallbackObserved == 0)
        {
            _diagnostic?.Invoke(
                "line-send-input-buffered",
                "kind=keyboard-fallback candidates=0");
        }
    }

    private void OnPointerDown(object? sender, PointerTrigger trigger)
    {
        if (_paused || trigger.ForegroundProcessId == 0 ||
            !string.Equals(
                _native.GetProcessName(trigger.ForegroundProcessId),
                LineContextValidator.ProcessName,
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

        if (!_pointerSendVerifier.IsPotentialSendControl(
                trigger.ScreenX,
                trigger.ScreenY,
                trigger.ForegroundProcessId,
                root))
        {
            return;
        }

        var composer = _composerTextReader.Read(
            root,
            trigger.ForegroundProcessId);

        lock (_candidateGate)
        {
            if (hasContext)
            {
                _recentSendSignals.Observe(
                    context.ContextHash,
                    trigger.OccurredAt,
                    composer.IsAvailable ? composer.Text : null);
            }

            _recentSendSignals.ObserveProcess(
                trigger.ForegroundProcessId,
                trigger.OccurredAt,
                composer.IsAvailable ? composer.Text : null);
        }

        var observed = hasContext
            ? MarkSendObserved(
                root,
                trigger.OccurredAt,
                "pointer",
                composer)
            : MarkSendObservedByProcess(
                trigger.ForegroundProcessId,
                trigger.OccurredAt,
                "pointer-fallback",
                composer);
        if (observed == 0)
        {
            _diagnostic?.Invoke(
                "line-send-input-buffered",
                $"kind={(hasContext ? "pointer" : "pointer-fallback")} candidates=0");
        }
    }

    private int MarkSendObserved(
        nint mainWindow,
        DateTimeOffset occurredAt,
        string inputKind,
        LineComposerTextSnapshot composer)
    {
        var observed = 0;
        var rejected = 0;
        lock (_candidateGate)
        {
            foreach (var candidate in _candidates.Where(candidate =>
                         candidate.Context.MainWindow == mainWindow &&
                         candidate.Context.OccurredAt <= occurredAt &&
                         !candidate.Cancellation.IsCancellationRequested))
            {
                if (!CanApplySendEvidence(candidate, composer))
                {
                    rejected++;
                    continue;
                }

                if (candidate.MarkSendObserved())
                {
                    observed++;
                }
            }
        }

        ReportRejectedSendEvidence(inputKind, rejected, composer.IsAvailable);

        if (observed > 0)
        {
            _diagnostic?.Invoke(
                "line-send-input-observed",
                $"kind={inputKind} candidates={observed}");
        }

        return observed;
    }

    private int MarkSendObservedByProcess(
        uint processId,
        DateTimeOffset occurredAt,
        string inputKind,
        LineComposerTextSnapshot composer)
    {
        var observed = 0;
        var rejected = 0;
        lock (_candidateGate)
        {
            foreach (var candidate in _candidates.Where(candidate =>
                         candidate.Context.ProcessId == processId &&
                         candidate.Context.OccurredAt <= occurredAt &&
                         !candidate.Cancellation.IsCancellationRequested))
            {
                if (!CanApplySendEvidence(candidate, composer))
                {
                    rejected++;
                    continue;
                }

                if (candidate.MarkSendObserved())
                {
                    observed++;
                }
            }
        }

        ReportRejectedSendEvidence(inputKind, rejected, composer.IsAvailable);

        if (observed > 0)
        {
            _diagnostic?.Invoke(
                "line-send-input-observed",
                $"kind={inputKind} candidates={observed}");
        }

        return observed;
    }

    private static bool CanApplySendEvidence(
        CandidateRegistration candidate,
        LineComposerTextSnapshot composer) =>
        candidate.Urls.Count == 0 ||
        (composer.IsAvailable &&
         LineMessageMatchPolicy.HasMatchingComposerEvidence(
             composer.Text,
             candidate.Urls));

    private void ReportRejectedSendEvidence(
        string inputKind,
        int rejected,
        bool composerAvailable)
    {
        if (rejected == 0)
        {
            return;
        }

        _diagnostic?.Invoke(
            "line-send-input-rejected",
            $"reason=composer-url-mismatch kind={inputKind} candidates={rejected} composerAvailable={composerAvailable}");
    }

    public async Task<LineNativeDropRegistrationResult>
        RegisterNativeDroppedFilesAsync(
            LineDropTarget target,
            IReadOnlyList<string> paths,
            DateTimeOffset occurredAt)
    {
        if (_paused)
        {
            return LineNativeDropRegistrationResult.Paused;
        }

        if (!_validator.TryValidate(
                target,
                _native.GetClipboardSequenceNumber(),
                occurredAt,
                out var context))
        {
            return LineNativeDropRegistrationResult.TargetInvalid;
        }

        var imagePaths = paths
            .Where(ClipboardImageCodec.IsSupportedImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (imagePaths.Length == 0)
        {
            return LineNativeDropRegistrationResult.UnsupportedFiles;
        }

        try
        {
            var images = await Task.Run(
                () => ClipboardImageCodec.TryReadFiles(imagePaths),
                _cancellation.Token);
            if (images.Count == 0)
            {
                return LineNativeDropRegistrationResult.ImageReadFailed;
            }

            var result = await _coordinator.CaptureBatchAsync(
                context.EventId,
                null,
                images.Select(image => new ImageCapturePayload(
                    image.ContentBytes,
                    image.Sha256,
                    image.PixelWidth,
                    image.PixelHeight,
                    image.MimeType,
                    image.FileExtension,
                    image.OriginalFileName)).ToList(),
                SourceApp.Line,
                CaptureMethod.LineConfirmedDrop,
                DeliveryStatus.Confirmed,
                context.ContextHash,
                occurredAt,
                [
                    "native-explorer-file-drop",
                    "line-drop-release-send"
                ],
                _cancellation.Token);
            var applied = result?.EventApplied == true;
            _diagnostic?.Invoke(
                "line-drop-candidate",
                $"registered={applied} files={imagePaths.Length} images={images.Count} confirmation=drop-release");
            if (!applied)
            {
                return LineNativeDropRegistrationResult.Duplicate;
            }

            Captured?.Invoke(
                this,
                new CaptureNotification(
                    images.Count > 1
                        ? ContentKind.Collection
                        : ContentKind.Image,
                    1,
                    occurredAt,
                    SourceApp.Line,
                    DeliveryStatus.Confirmed));
            _diagnostic?.Invoke(
                "line-capture-applied",
                $"urls=0 images={images.Count} drop=True confirmation=drop-release");
            return LineNativeDropRegistrationResult.Registered;
        }
        catch (OperationCanceledException)
            when (_cancellation.IsCancellationRequested)
        {
            return LineNativeDropRegistrationResult.Paused;
        }
        catch (Exception exception)
        {
            ReportIssue(exception);
            return LineNativeDropRegistrationResult.Failed;
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

        var baseline = await _accessibility.TryCaptureAsync(
            context,
            requireFocusedComposer: true,
            allowImageSendDialog: true,
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

        if (baseline.ImageSendDialogFocused && images.Count == 0)
        {
            _diagnostic?.Invoke(
                "line-context-rejected",
                "reason=image-send-dialog-without-image");
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
            "line-paste-candidate",
            $"registered={registered} urls={urls.Count} images={images.Count}");
    }

    private bool StartCandidate(
        ValidatedLineContext context,
        LineAccessibilitySnapshot baseline,
        string? clipboardText,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images,
        bool nativeDrop)
    {
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
            var registration = new CandidateRegistration(
                context,
                baseline,
                clipboardText,
                candidateUrls,
                candidateImages,
                nativeDrop,
                cancellation);
            _candidates.Add(registration);
            if (_recentSendSignals.CanApply(
                    context.ContextHash,
                    context.ProcessId,
                    context.OccurredAt,
                    DateTimeOffset.UtcNow,
                    candidateUrls,
                    candidateImages.Count > 0))
            {
                registration.MarkSendObserved();
                _diagnostic?.Invoke(
                    "line-send-input-replayed",
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
            var response = await _accessibility.WaitForConfirmationAsync(
                new LineConfirmationRequest(
                    registration.Context,
                    registration.Baseline,
                    registration.Urls,
                    registration.Images.Count > 0,
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
                SourceApp.Line,
                registration.NativeDrop
                    ? CaptureMethod.LineConfirmedDrop
                    : registration.Images.Count > 0
                        ? CaptureMethod.LineConfirmedImage
                        : CaptureMethod.LineConfirmedSend,
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
                    SourceApp.Line,
                    DeliveryStatus.Confirmed));
            _diagnostic?.Invoke(
                "line-capture-applied",
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
                foreach (var url in registration.Urls)
                {
                    _activeUrls.Remove(url.Value);
                }

                foreach (var image in registration.Images)
                {
                    _activeImageHashes.Remove(image.Sha256);
                }
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
            "line-capture-failed",
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
                "line-capture-item-failed",
                "LINE 입력 일부를 처리하지 못했지만 감지는 계속됩니다.",
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
        ValidatedLineContext context,
        LineAccessibilitySnapshot baseline,
        string? clipboardText,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images,
        bool nativeDrop,
        CancellationTokenSource cancellation)
    {
        private int _sendObserved;

        public ValidatedLineContext Context { get; } = context;
        public LineAccessibilitySnapshot Baseline { get; } = baseline;
        public string? ClipboardText { get; } = clipboardText;
        public IReadOnlyList<NormalizedUrl> Urls { get; } = urls;
        public IReadOnlyList<ClipboardImageSnapshot> Images { get; } = images;
        public bool NativeDrop { get; } = nativeDrop;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = Task.CompletedTask;

        public bool MarkSendObserved() =>
            Interlocked.Exchange(ref _sendObserved, 1) == 0;

        public bool IsSendObserved() =>
            Volatile.Read(ref _sendObserved) == 1;
    }
}
