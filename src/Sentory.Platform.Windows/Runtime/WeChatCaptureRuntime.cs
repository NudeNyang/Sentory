using System.Threading.Channels;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public enum WeChatNativeDropRegistrationResult
{
    Registered,
    Paused,
    TargetInvalid,
    ContextUnavailable,
    UnsupportedFiles,
    ImageReadFailed,
    Duplicate,
    Failed
}

internal static class WeChatCaptureMethodPolicy
{
    public static CaptureMethod Select(bool hasImages, bool nativeDrop) =>
        nativeDrop
            ? CaptureMethod.WeChatConfirmedDrop
            : hasImages
                ? CaptureMethod.WeChatConfirmedImage
                : CaptureMethod.WeChatConfirmedSend;
}

public sealed class WeChatCaptureRuntime : ICaptureRuntime
{
    private const int MaximumActiveCandidates = 8;

    private readonly INativeWindowApi _native;
    private readonly WeChatContextValidator _validator;
    private readonly LowLevelPasteHook _keyboardHook;
    private readonly LowLevelMouseHook _mouseHook;
    private readonly StaClipboardReader _clipboardReader;
    private readonly IWeChatAccessibilityClient _accessibility;
    private readonly IWeChatPointerSendVerifier _pointerSendVerifier;
    private readonly IWeChatComposerTextReader _composerTextReader;
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
    private readonly WeChatRecentSendSignals _recentSendSignals = new();
    private Task? _worker;
    private volatile bool _paused;
    private DateTimeOffset _lastIssueReportedAt = DateTimeOffset.MinValue;

    public WeChatCaptureRuntime(
        ICaptureRepository repository,
        bool acceptInjectedInput = false,
        Action<string, string>? diagnostic = null)
    {
        var native = new NativeWindowApi();
        _native = native;
        _validator = new WeChatContextValidator(native);
        _keyboardHook = new LowLevelPasteHook(native, acceptInjectedInput);
        _mouseHook = new LowLevelMouseHook(native, acceptInjectedInput);
        _clipboardReader = new StaClipboardReader(native);
        _accessibility = new WeChatAccessibilityClient(native, diagnostic);
        _pointerSendVerifier = new WeChatPointerSendVerifier();
        _composerTextReader = new WeChatComposerTextReader();
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
            !WeChatContextValidator.IsSupportedProcessName(
                _native.GetProcessName(trigger.ForegroundProcessId)))
        {
            return;
        }

        if (_validator.TryValidate(trigger, out var context))
        {
            var composer = _composerTextReader.Read(
                context.MainWindow,
                context.ProcessId);
            ObserveSendSignal(
                context,
                context.OccurredAt,
                composer);
            var observed = MarkSendObserved(
                context.MainWindow,
                context.OccurredAt,
                "keyboard",
                composer);
            ReportBufferedSend("keyboard", observed);
            return;
        }

        var root = _native.GetRootWindow(trigger.ForegroundWindow);
        var fallbackComposer = root == nint.Zero
            ? new WeChatComposerTextSnapshot(false, string.Empty)
            : _composerTextReader.Read(root, trigger.ForegroundProcessId);
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
        ReportBufferedSend("keyboard-fallback", fallbackObserved);
    }

    private void OnPointerDown(object? sender, PointerTrigger trigger)
    {
        if (_paused || trigger.ForegroundProcessId == 0 ||
            !WeChatContextValidator.IsSupportedProcessName(
                _native.GetProcessName(trigger.ForegroundProcessId)))
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
        if (root == nint.Zero ||
            !_pointerSendVerifier.IsPotentialSendControl(
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
        if (hasContext)
        {
            ObserveSendSignal(context, trigger.OccurredAt, composer);
        }
        else
        {
            lock (_candidateGate)
            {
                _recentSendSignals.ObserveProcess(
                    trigger.ForegroundProcessId,
                    trigger.OccurredAt,
                    composer.IsAvailable ? composer.Text : null);
            }
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
        ReportBufferedSend(
            hasContext ? "pointer" : "pointer-fallback",
            observed);
    }

    private void ObserveSendSignal(
        ValidatedWeChatContext context,
        DateTimeOffset occurredAt,
        WeChatComposerTextSnapshot composer)
    {
        lock (_candidateGate)
        {
            var text = composer.IsAvailable ? composer.Text : null;
            _recentSendSignals.Observe(
                context.ContextHash,
                occurredAt,
                text);
            _recentSendSignals.ObserveProcess(
                context.ProcessId,
                occurredAt,
                text);
        }
    }

    private int MarkSendObserved(
        nint mainWindow,
        DateTimeOffset occurredAt,
        string inputKind,
        WeChatComposerTextSnapshot composer)
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
                "wechat-send-input-observed",
                $"kind={inputKind} candidates={observed}");
        }

        return observed;
    }

    private int MarkSendObservedByProcess(
        uint processId,
        DateTimeOffset occurredAt,
        string inputKind,
        WeChatComposerTextSnapshot composer)
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
                "wechat-send-input-observed",
                $"kind={inputKind} candidates={observed}");
        }

        return observed;
    }

    private static bool CanApplySendEvidence(
        CandidateRegistration candidate,
        WeChatComposerTextSnapshot composer) =>
        candidate.Urls.Count == 0 ||
        (composer.IsAvailable &&
         WeChatMessageMatchPolicy.HasMatchingComposerEvidence(
             composer.Text,
             candidate.Urls));

    private void ReportRejectedSendEvidence(
        string inputKind,
        int rejected,
        bool composerAvailable)
    {
        if (rejected > 0)
        {
            _diagnostic?.Invoke(
                "wechat-send-input-rejected",
                $"reason=composer-url-mismatch kind={inputKind} candidates={rejected} composerAvailable={composerAvailable}");
        }
    }

    private void ReportBufferedSend(string inputKind, int observed)
    {
        if (observed == 0)
        {
            _diagnostic?.Invoke(
                "wechat-send-input-buffered",
                $"kind={inputKind} candidates=0");
        }
    }

    public async Task<WeChatNativeDropRegistrationResult>
        RegisterNativeDroppedFilesAsync(
            WeChatDropTarget target,
            IReadOnlyList<string> paths,
            DateTimeOffset occurredAt)
    {
        if (_paused)
        {
            return WeChatNativeDropRegistrationResult.Paused;
        }

        if (!_validator.TryValidate(
                target,
                _native.GetClipboardSequenceNumber(),
                occurredAt,
                out var context))
        {
            return WeChatNativeDropRegistrationResult.TargetInvalid;
        }

        var imagePaths = paths
            .Where(ClipboardImageCodec.IsSupportedImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (imagePaths.Length == 0)
        {
            return WeChatNativeDropRegistrationResult.UnsupportedFiles;
        }

        try
        {
            var baseline = await _accessibility.TryCaptureAsync(
                context,
                requireFocusedComposer: false,
                _cancellation.Token);
            if (baseline is null)
            {
                return WeChatNativeDropRegistrationResult.ContextUnavailable;
            }

            var images = await Task.Run(
                () => ClipboardImageCodec.TryReadFiles(imagePaths),
                _cancellation.Token);
            if (images.Count == 0)
            {
                return WeChatNativeDropRegistrationResult.ImageReadFailed;
            }

            var registered = StartCandidate(
                context,
                baseline,
                null,
                [],
                images,
                nativeDrop: true);
            _diagnostic?.Invoke(
                "wechat-drop-candidate",
                $"registered={registered} files={imagePaths.Length} images={images.Count} confirmation=new-message");
            return registered
                ? WeChatNativeDropRegistrationResult.Registered
                : WeChatNativeDropRegistrationResult.Duplicate;
        }
        catch (OperationCanceledException)
            when (_cancellation.IsCancellationRequested)
        {
            return WeChatNativeDropRegistrationResult.Paused;
        }
        catch (Exception exception)
        {
            ReportIssue(exception);
            return WeChatNativeDropRegistrationResult.Failed;
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
            images);
        _diagnostic?.Invoke(
            "wechat-paste-candidate",
            $"registered={registered} urls={urls.Count} images={images.Count}");
    }

    private bool StartCandidate(
        ValidatedWeChatContext context,
        WeChatAccessibilitySnapshot baseline,
        string? clipboardText,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images,
        bool nativeDrop = false)
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
                    "wechat-send-input-replayed",
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
                new WeChatConfirmationRequest(
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
                SourceApp.WeChat,
                WeChatCaptureMethodPolicy.Select(
                    registration.Images.Count > 0,
                    registration.NativeDrop),
                DeliveryStatus.Confirmed,
                registration.Context.ContextHash,
                capturedAt,
                response.Signals
                    .Append(registration.NativeDrop
                        ? "native-explorer-file-drop"
                        : "ctrl-v")
                    .ToList(),
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
                    SourceApp.WeChat,
                    DeliveryStatus.Confirmed));
            _diagnostic?.Invoke(
                "wechat-capture-applied",
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
            "wechat-capture-failed",
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
                "wechat-capture-item-failed",
                "WeChat 입력 일부를 처리하지 못했지만 감지는 계속됩니다.",
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
        ValidatedWeChatContext context,
        WeChatAccessibilitySnapshot baseline,
        string? clipboardText,
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images,
        bool nativeDrop,
        CancellationTokenSource cancellation)
    {
        private int _sendObserved;

        public ValidatedWeChatContext Context { get; } = context;
        public WeChatAccessibilitySnapshot Baseline { get; } = baseline;
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
