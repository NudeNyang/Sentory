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
    private static readonly TimeSpan NativeDropBaselineMaximumAge =
        TimeSpan.FromSeconds(10);

    private readonly INativeWindowApi _native;
    private readonly IKakaoDropWindowApi _dropWindows;
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
    private readonly List<ValidatedLineContext> _pendingNativeDropContexts = [];
    private readonly LineRecentSendSignals _recentSendSignals = new();
    private readonly LineNativeDropBaselineCache _nativeDropBaselineCache =
        new(NativeDropBaselineMaximumAge);
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
        _dropWindows = native;
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
                _nativeDropBaselineCache.Clear();
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
        ExplorerPointerDownOriginTracker.ObserveShared(_native, trigger);
        if (_paused)
        {
            return;
        }

        var pointWindow = _dropWindows.GetWindowAtPoint(
            trigger.ScreenX,
            trigger.ScreenY);
        var pointRoot = _native.GetRootWindow(pointWindow);
        var pointProcessId = pointRoot == nint.Zero
            ? 0
            : _native.GetProcessId(pointRoot);
        var pointProcessName = pointProcessId == 0
            ? null
            : _native.GetProcessName(pointProcessId);
        var pointIsLine = IsLineProcessName(pointProcessName);
        var foregroundIsLine = IsLineProcess(trigger.ForegroundProcessId);
        if (!pointIsLine && !foregroundIsLine)
        {
            var pendingContext = FindLatestPendingImageContextAtPointer(
                trigger.ScreenX,
                trigger.ScreenY,
                trigger.OccurredAt);
            if (pendingContext is not null)
            {
                ReportRejectedPointer(
                    trigger,
                    _native.GetWindowBounds(pendingContext.MainWindow),
                    pointIsLine: false,
                    foregroundIsLine: false,
                    pointProcessName: pointProcessName,
                    reason: "non-line-surface");
            }

            return;
        }

        // WM_LBUTTONDOWN 시점에는 새로 누른 창보다 직전 foreground가
        // 보고될 수 있다. 실제 포인터 아래 LINE 창을 우선해야, 사진
        // 미리보기를 잠시 둔 뒤 누르는 전송 버튼도 놓치지 않는다.
        var pointerProcessId = pointIsLine
            ? pointProcessId
            : trigger.ForegroundProcessId;
        var pendingImageContext = FindLatestPendingImageContext(
            pointerProcessId,
            trigger.OccurredAt);

        var hasValidatedContext = _validator.TryValidate(
                new PasteTrigger(
                    trigger.EventId,
                    pointIsLine ? pointRoot : trigger.ForegroundWindow,
                    pointIsLine ? pointRoot : trigger.ForegroundWindow,
                    pointerProcessId,
                    _native.GetClipboardSequenceNumber(),
                    trigger.OccurredAt,
                    trigger.Injected),
                out var validatedContext);
        var context = pendingImageContext ??
                      (hasValidatedContext ? validatedContext : null);
        var root = context?.MainWindow ?? nint.Zero;
        if (root == nint.Zero)
        {
            return;
        }

        var verifiedSendControl =
            _pointerSendVerifier.IsPotentialSendControl(
                trigger.ScreenX,
                trigger.ScreenY,
                pointerProcessId,
                root);
        var pointerSurfaceRoot = pointIsLine
            ? pointRoot
            : _native.GetRootWindow(trigger.ForegroundWindow);
        var pointerSurfaceBounds = _native.GetWindowBounds(
            pointerSurfaceRoot != nint.Zero
                ? pointerSurfaceRoot
                : root);
        var withinImageDialogRegion = pendingImageContext is not null &&
                                      LineImageDialogSendButtonPolicy.IsWithin(
                                          pointerSurfaceBounds,
                                          trigger.ScreenX,
                                          trigger.ScreenY);
        var imageDialogRegionFallback = !verifiedSendControl &&
                                        withinImageDialogRegion;
        if (!verifiedSendControl && !imageDialogRegionFallback)
        {
            if (pendingImageContext is not null)
            {
                ReportRejectedPointer(
                    trigger,
                    pointerSurfaceBounds,
                    pointIsLine,
                    foregroundIsLine,
                    pointProcessName);
            }

            return;
        }

        var inputKind = imageDialogRegionFallback
            ? "pointer-region-fallback"
            : "pointer";

        var composer = _composerTextReader.Read(
            root,
            pointerProcessId);

        lock (_candidateGate)
        {
            if (context is not null)
            {
                _recentSendSignals.Observe(
                    context.ContextHash,
                    trigger.OccurredAt,
                    composer.IsAvailable ? composer.Text : null,
                    withinImageDialogRegion);
            }

            _recentSendSignals.ObserveProcess(
                pointerProcessId,
                trigger.OccurredAt,
                composer.IsAvailable ? composer.Text : null,
                withinImageDialogRegion);
        }

        var observed = pendingImageContext is not null
            ? MarkCandidateSendObserved(
                pendingImageContext.EventId,
                trigger.OccurredAt,
                inputKind,
                composer,
                withinImageDialogRegion)
            : context is not null
                ? MarkSendObserved(
                    root,
                    trigger.OccurredAt,
                    inputKind,
                    composer,
                    withinImageDialogRegion)
                : MarkSendObservedByProcess(
                pointerProcessId,
                trigger.OccurredAt,
                imageDialogRegionFallback
                    ? "pointer-region-fallback"
                    : "pointer-fallback",
                composer,
                withinImageDialogRegion);
        if (observed == 0)
        {
            _diagnostic?.Invoke(
                "line-send-input-buffered",
                $"kind={(context is not null ? inputKind : imageDialogRegionFallback ? "pointer-region-fallback" : "pointer-fallback")} candidates=0");
        }
    }

    private bool IsLineProcess(uint processId) =>
        processId != 0 &&
        IsLineProcessName(_native.GetProcessName(processId));

    private static bool IsLineProcessName(string? processName) =>
        string.Equals(
            processName,
            LineContextValidator.ProcessName,
            StringComparison.OrdinalIgnoreCase);

    private ValidatedLineContext? FindLatestPendingImageContext(
        uint processId,
        DateTimeOffset occurredAt)
    {
        lock (_candidateGate)
        {
            var candidateContext = _candidates
                .Where(candidate =>
                    candidate.Context.ProcessId == processId &&
                    candidate.Context.OccurredAt <= occurredAt &&
                    candidate.Images.Count > 0 &&
                    !candidate.IsSendObserved() &&
                    !candidate.Cancellation.IsCancellationRequested)
                .OrderByDescending(candidate => candidate.Context.OccurredAt)
                .Select(candidate => candidate.Context)
                .FirstOrDefault();
            return candidateContext ??
                   _pendingNativeDropContexts
                       .Where(context =>
                           context.ProcessId == processId &&
                           context.OccurredAt <= occurredAt)
                       .OrderByDescending(context => context.OccurredAt)
                       .FirstOrDefault();
        }
    }

    private ValidatedLineContext? FindLatestPendingImageContextAtPointer(
        int screenX,
        int screenY,
        DateTimeOffset occurredAt)
    {
        ValidatedLineContext[] contexts;
        lock (_candidateGate)
        {
            contexts = _candidates
                .Where(candidate =>
                    candidate.Context.OccurredAt <= occurredAt &&
                    candidate.Images.Count > 0 &&
                    !candidate.IsSendObserved() &&
                    !candidate.Cancellation.IsCancellationRequested)
                .Select(candidate => candidate.Context)
                .Concat(_pendingNativeDropContexts.Where(context =>
                    context.OccurredAt <= occurredAt))
                .DistinctBy(context => context.EventId)
                .OrderByDescending(context => context.OccurredAt)
                .ToArray();
        }

        return contexts.FirstOrDefault(context =>
        {
            var bounds = _native.GetWindowBounds(context.MainWindow);
            return bounds.Width > 0 &&
                   bounds.Height > 0 &&
                   screenX >= bounds.Left &&
                   screenX < bounds.Right &&
                   screenY >= bounds.Top &&
                   screenY < bounds.Bottom;
        });
    }

    private void ReportRejectedPointer(
        PointerTrigger trigger,
        WindowBounds bounds,
        bool pointIsLine,
        bool foregroundIsLine,
        string? pointProcessName,
        string reason = "unverified")
    {
        var xPermille = bounds.Width > 0
            ? (trigger.ScreenX - bounds.Left) * 1000 / bounds.Width
            : -1;
        var yPermille = bounds.Height > 0
            ? (trigger.ScreenY - bounds.Top) * 1000 / bounds.Height
            : -1;
        _diagnostic?.Invoke(
            "line-send-pointer-rejected",
            $"reason={reason} pointLine={pointIsLine} foregroundLine={foregroundIsLine} pointProcess={NormalizeProcessDiagnostic(pointProcessName)} xPermille={xPermille} yPermille={yPermille}");
    }

    private static string NormalizeProcessDiagnostic(string? processName) =>
        IsLineProcessName(processName)
            ? LineContextValidator.ProcessName
            : string.IsNullOrWhiteSpace(processName)
                ? "none"
                : "other";

    private int MarkCandidateSendObserved(
        Guid eventId,
        DateTimeOffset occurredAt,
        string inputKind,
        LineComposerTextSnapshot composer,
        bool imageDialogSendObserved = false)
    {
        var observed = 0;
        var rejected = 0;
        lock (_candidateGate)
        {
            var candidate = _candidates.FirstOrDefault(candidate =>
                candidate.Context.EventId == eventId &&
                candidate.Context.OccurredAt <= occurredAt &&
                !candidate.Cancellation.IsCancellationRequested);
            if (candidate is not null)
            {
                if (!CanApplySendEvidence(candidate, composer))
                {
                    rejected = 1;
                }
                else if (!candidate.IsSendObserved() &&
                         _recentSendSignals.TryConsume(occurredAt) &&
                         candidate.MarkSendObserved(
                             imageDialogSendObserved))
                {
                    observed = 1;
                }
            }
        }

        ReportRejectedSendEvidence(inputKind, rejected, composer.IsAvailable);
        if (observed > 0)
        {
            _diagnostic?.Invoke(
                "line-send-input-observed",
                $"kind={inputKind} candidates=1");
        }

        return observed;
    }

    private int MarkSendObserved(
        nint mainWindow,
        DateTimeOffset occurredAt,
        string inputKind,
        LineComposerTextSnapshot composer,
        bool imageDialogSendObserved = false)
    {
        var observed = 0;
        var rejected = 0;
        lock (_candidateGate)
        {
            var candidates = _candidates.Where(candidate =>
                    candidate.Context.MainWindow == mainWindow &&
                    candidate.Context.OccurredAt <= occurredAt &&
                    !candidate.IsSendObserved() &&
                    !candidate.Cancellation.IsCancellationRequested)
                .ToArray();
            rejected = candidates.Count(candidate =>
                !CanApplySendEvidence(candidate, composer));
            var candidate = MessengerSendCandidatePolicy.SelectLatestEligible(
                candidates,
                candidate => CanApplySendEvidence(candidate, composer),
                candidate => candidate.Context.OccurredAt);
            if (candidate is not null &&
                _recentSendSignals.TryConsume(occurredAt) &&
                candidate.MarkSendObserved(
                    imageDialogSendObserved))
            {
                observed = 1;
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
        LineComposerTextSnapshot composer,
        bool imageDialogSendObserved = false)
    {
        var observed = 0;
        var rejected = 0;
        lock (_candidateGate)
        {
            var candidates = _candidates.Where(candidate =>
                    candidate.Context.ProcessId == processId &&
                    candidate.Context.OccurredAt <= occurredAt &&
                    !candidate.IsSendObserved() &&
                    !candidate.Cancellation.IsCancellationRequested)
                .ToArray();
            rejected = candidates.Count(candidate =>
                !CanApplySendEvidence(candidate, composer));
            var candidate = MessengerSendCandidatePolicy.SelectLatestEligible(
                candidates,
                candidate => CanApplySendEvidence(candidate, composer),
                candidate => candidate.Context.OccurredAt);
            if (candidate is not null &&
                _recentSendSignals.TryConsume(occurredAt) &&
                candidate.MarkSendObserved(
                    imageDialogSendObserved))
            {
                observed = 1;
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
            DateTimeOffset occurredAt) =>
        await RegisterNativeDroppedFilesAsync(
            target,
            paths,
            occurredAt,
            preDropBaseline: null);

    internal async Task<LineNativeDropRegistrationResult>
        RegisterNativeDroppedFilesAsync(
            LineDropTarget target,
            IReadOnlyList<string> paths,
            DateTimeOffset occurredAt,
            LineAccessibilitySnapshot? preDropBaseline)
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

        lock (_candidateGate)
        {
            _pendingNativeDropContexts.Add(context);
        }

        try
        {
            var baseline = preDropBaseline;
            if (baseline is null &&
                _nativeDropBaselineCache.TryGetLastKnown(
                    target,
                    DateTimeOffset.UtcNow,
                    out var lastKnown,
                    out var lastKnownAge))
            {
                baseline = lastKnown;
                _diagnostic?.Invoke(
                    "line-drop-baseline-cache-recovered",
                    $"ageMs={(long)lastKnownAge.TotalMilliseconds} messages={lastKnown.MessageIds.Count}");
            }

            baseline ??= new LineAccessibilitySnapshot(
                string.Empty,
                new HashSet<string>(StringComparer.Ordinal),
                IsUnanchored: true);

            var images = await Task.Run(
                () => ClipboardImageCodec.TryReadFiles(imagePaths),
                _cancellation.Token);
            if (images.Count == 0)
            {
                return LineNativeDropRegistrationResult.ImageReadFailed;
            }

            var registered = StartCandidate(
                context,
                baseline,
                null,
                [],
                images,
                nativeDrop: true);
            _diagnostic?.Invoke(
                "line-drop-candidate",
                $"registered={registered} files={imagePaths.Length} images={images.Count} confirmation=explicit-send-and-new-message preDropBaseline={preDropBaseline is not null}");
            return registered
                ? LineNativeDropRegistrationResult.Registered
                : LineNativeDropRegistrationResult.Duplicate;
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
        finally
        {
            lock (_candidateGate)
            {
                _pendingNativeDropContexts.RemoveAll(pending =>
                    pending.EventId == context.EventId);
            }
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

    internal async Task<LineAccessibilitySnapshot?>
        TryCaptureNativeDropBaselineAsync(
            LineDropTarget target,
            DateTimeOffset occurredAt)
    {
        if (_nativeDropBaselineCache.TryGet(
                target,
                DateTimeOffset.UtcNow,
                out var cached,
                out var age))
        {
            _diagnostic?.Invoke(
                "line-drop-baseline-cache-used",
                $"ageMs={(int)age.TotalMilliseconds} messages={cached.MessageIds.Count}");
            return cached;
        }

        var current = await CaptureNativeDropBaselineAsync(
            target,
            occurredAt,
            reportDiagnostics: true);
        if (current is not null)
        {
            return current;
        }

        if (_nativeDropBaselineCache.TryGetLastKnown(
                target,
                DateTimeOffset.UtcNow,
                out var lastKnown,
                out age))
        {
            _diagnostic?.Invoke(
                "line-drop-baseline-cache-recovered",
                $"ageMs={(long)age.TotalMilliseconds} messages={lastKnown.MessageIds.Count}");
            return lastKnown;
        }

        return null;
    }

    internal async Task RefreshNativeDropBaselineAsync(
        LineDropTarget target,
        DateTimeOffset occurredAt) =>
        _ = await CaptureNativeDropBaselineAsync(
            target,
            occurredAt,
            reportDiagnostics: false);

    private async Task<LineAccessibilitySnapshot?>
        CaptureNativeDropBaselineAsync(
            LineDropTarget target,
            DateTimeOffset occurredAt,
            bool reportDiagnostics)
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
            var snapshot = await _accessibility.TryCaptureAsync(
                context,
                requireFocusedComposer: false,
                allowImageSendDialog: false,
                _cancellation.Token,
                reportDiagnostics);
            if (snapshot is not null)
            {
                _nativeDropBaselineCache.Observe(
                    target,
                    snapshot,
                    DateTimeOffset.UtcNow);
            }

            return snapshot;
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
                    DateTimeOffset.UtcNow,
                    candidateUrls,
                    candidateImages.Count > 0,
                    out var recentSendSignal))
            {
                registration.MarkSendObserved(
                    recentSendSignal.ImageDialogSendObserved);
                _diagnostic?.Invoke(
                    "line-send-input-replayed",
                    $"kind=buffered candidates=1 imageDialog={recentSendSignal.ImageDialogSendObserved}");
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
                registration.IsImageDialogSendObserved,
                registration.Cancellation.Token);
            if (!response.Confirmed || _paused)
            {
                return;
            }

            if (response.ObservedSnapshot is not null)
            {
                _nativeDropBaselineCache.Observe(
                    new LineDropTarget(
                        registration.Context.MainWindow,
                        registration.Context.ProcessId,
                        default),
                    response.ObservedSnapshot,
                    DateTimeOffset.UtcNow);
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
        string payloadSignature,
        CancellationTokenSource cancellation)
    {
        private int _sendObserved;
        private int _imageDialogSendObserved;

        public ValidatedLineContext Context { get; } = context;
        public LineAccessibilitySnapshot Baseline { get; } = baseline;
        public string? ClipboardText { get; } = clipboardText;
        public IReadOnlyList<NormalizedUrl> Urls { get; } = urls;
        public IReadOnlyList<ClipboardImageSnapshot> Images { get; } = images;
        public bool NativeDrop { get; } = nativeDrop;
        public string PayloadSignature { get; } = payloadSignature;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = Task.CompletedTask;

        public bool MarkSendObserved(bool imageDialogSendObserved = false)
        {
            if (imageDialogSendObserved)
            {
                Interlocked.Exchange(ref _imageDialogSendObserved, 1);
            }

            return Interlocked.Exchange(ref _sendObserved, 1) == 0;
        }

        public bool IsSendObserved() =>
            Volatile.Read(ref _sendObserved) == 1;

        public bool IsImageDialogSendObserved() =>
            Volatile.Read(ref _imageDialogSendObserved) == 1;
    }
}
