using System.Threading.Channels;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public enum KakaoNativeDropCaptureResult
{
    Captured,
    Paused,
    UnsupportedFiles,
    TargetInvalid,
    ImageReadFailed,
    StorageNotApplied,
    Failed
}

public sealed class KakaoCaptureRuntime : ICaptureRuntime
{
    private readonly INativeWindowApi _native;
    private readonly KakaoContextValidator _validator;
    private readonly KakaoImageConfirmationValidator _imageValidator;
    private readonly LowLevelPasteHook _hook;
    private readonly StaClipboardReader _clipboardReader;
    private readonly KakaoInputValueVerifier _inputVerifier;
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
    private Task? _worker;
    private volatile bool _paused;
    private DateTimeOffset _lastIssueReportedAt = DateTimeOffset.MinValue;

    public KakaoCaptureRuntime(
        ICaptureRepository repository,
        bool acceptInjectedInput = false)
    {
        _native = new NativeWindowApi();
        _validator = new KakaoContextValidator(_native);
        _imageValidator = new KakaoImageConfirmationValidator(_native);
        _hook = new LowLevelPasteHook(_native, acceptInjectedInput);
        _clipboardReader = new StaClipboardReader(_native);
        _inputVerifier = new KakaoInputValueVerifier();
        _coordinator = new CaptureCoordinator(repository);
    }

    public event EventHandler<CaptureNotification>? Captured;

    public event EventHandler<CaptureRuntimeIssue>? IssueDetected;

    public bool IsPaused
    {
        get => _paused;
        set => _paused = value;
    }

    public void Start()
    {
        if (_worker is not null)
        {
            return;
        }

        _hook.PasteDetected += OnPasteDetected;
        _worker = Task.Run(() => ProcessTriggersAsync(_cancellation.Token));
        _hook.Start();
    }

    private void OnPasteDetected(object? sender, PasteTrigger trigger)
    {
        _triggers.Writer.TryWrite(trigger);
    }

    public async Task<KakaoNativeDropCaptureResult> CaptureNativeDroppedFilesAsync(
        KakaoDropTarget target,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        if (_paused)
        {
            return KakaoNativeDropCaptureResult.Paused;
        }

        var supportedPaths = filePaths
            .Where(ClipboardImageCodec.IsSupportedImagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (supportedPaths.Length == 0)
        {
            return KakaoNativeDropCaptureResult.UnsupportedFiles;
        }

        try
        {
            var clipboardSequence = _native.GetClipboardSequenceNumber();
            var occurredAt = DateTimeOffset.UtcNow;
            if (!_validator.TryValidateTarget(
                    target,
                    clipboardSequence,
                    occurredAt,
                    out var context))
            {
                return KakaoNativeDropCaptureResult.TargetInvalid;
            }

            var images = await Task.Run(
                () => ClipboardImageCodec.TryReadFiles(supportedPaths),
                cancellationToken);
            if (images.Count == 0)
            {
                return KakaoNativeDropCaptureResult.ImageReadFailed;
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
                SourceApp.KakaoTalk,
                CaptureMethod.KakaoDragDrop,
                DeliveryStatus.NotObserved,
                context.ContextHash,
                context.OccurredAt,
                [
                    "sentory-pass-through-drop-target",
                    "native-explorer-file-drop",
                    "kakao-process",
                    "individual-chat-root",
                    "input-class-and-id",
                    "message-list-class-and-id",
                    "release-over-same-chat-root",
                    "escape-not-observed",
                    "exact-file-paths"
                ],
                cancellationToken);
            if (result?.EventApplied != true)
            {
                return KakaoNativeDropCaptureResult.StorageNotApplied;
            }

            Captured?.Invoke(
                this,
                new CaptureNotification(
                    images.Count > 1
                        ? ContentKind.Collection
                        : ContentKind.Image,
                    1,
                    context.OccurredAt,
                    SourceApp.KakaoTalk,
                    DeliveryStatus.NotObserved));
            return KakaoNativeDropCaptureResult.Captured;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportIssue(exception);
            return KakaoNativeDropCaptureResult.Failed;
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

        var clipboard = await _clipboardReader.ReadAsync(
            context.ClipboardSequenceNumber,
            cancellationToken);
        if (clipboard is null)
        {
            return;
        }

        if (clipboard.Images.Count > 0)
        {
            await CaptureImagesIfConfirmedAsync(
                context,
                clipboard.Text,
                clipboard.Images,
                cancellationToken);
            return;
        }

        if (clipboard.Text is null ||
            UrlExtractor.Extract(clipboard.Text).Count == 0)
        {
            return;
        }

        await Task.Delay(150, cancellationToken);
        if (!StillMatches(context))
        {
            return;
        }

        var matchesInput =
            await _inputVerifier.ContainsClipboardUrlsAsync(
                context.InputWindow,
                clipboard.Text,
                TimeSpan.FromMilliseconds(750),
                cancellationToken);
        if (!matchesInput)
        {
            return;
        }

        var result = await _coordinator.CaptureBatchAsync(
            context.EventId,
            clipboard.Text,
            [],
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVUrl,
            DeliveryStatus.NotObserved,
            context.ContextHash,
            context.OccurredAt,
            [
                "ctrl-v",
                "kakao-process",
                "individual-chat-root",
                "input-class-and-id",
                "message-list-class-and-id",
                "clipboard-sequence-stable",
                "input-value-url-match"
            ],
            cancellationToken);
        if (result?.EventApplied == true)
        {
            Captured?.Invoke(
                this,
                new CaptureNotification(
                    UrlExtractor.Extract(clipboard.Text).DistinctBy(url => url.Value).Count() > 1
                        ? ContentKind.Collection
                        : ContentKind.Url,
                    1,
                    context.OccurredAt,
                    SourceApp.KakaoTalk,
                    DeliveryStatus.NotObserved));
        }
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
                "kakao-capture-item-failed",
                "일부 입력을 처리하지 못했지만 감지는 계속됩니다.",
                now));
    }

    private async Task CaptureImagesIfConfirmedAsync(
        ValidatedKakaoContext context,
        string? clipboardText,
        IReadOnlyList<ClipboardImageSnapshot> images,
        CancellationToken cancellationToken)
    {
        var confirmed = false;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            await Task.Delay(50, cancellationToken);
            if (_imageValidator.TryValidate(context, out _))
            {
                confirmed = true;
                break;
            }
        }

        if (!confirmed)
        {
            return;
        }

        var result = await _coordinator.CaptureBatchAsync(
            context.EventId,
            clipboardText,
            images.Select(image => new ImageCapturePayload(
                image.ContentBytes,
                image.Sha256,
                image.PixelWidth,
                image.PixelHeight,
                image.MimeType,
                image.FileExtension,
                image.OriginalFileName)).ToList(),
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVImage,
            DeliveryStatus.NotObserved,
            context.ContextHash,
            context.OccurredAt,
            [
                "ctrl-v",
                "kakao-process",
                "individual-chat-root",
                "input-class-and-id",
                "message-list-class-and-id",
                "clipboard-image-sequence-stable",
                "owned-image-confirmation-window",
                "caption-edit-class-and-id"
            ],
            cancellationToken);

        if (result?.EventApplied == true)
        {
            var memberCount = images
                .DistinctBy(image => image.Sha256, StringComparer.OrdinalIgnoreCase)
                .Count() + UrlExtractor.Extract(clipboardText ?? string.Empty)
                .DistinctBy(url => url.Value)
                .Count();
            Captured?.Invoke(
                this,
                new CaptureNotification(
                    memberCount > 1
                        ? ContentKind.Collection
                        : ContentKind.Image,
                    1,
                    context.OccurredAt,
                    SourceApp.KakaoTalk,
                    DeliveryStatus.NotObserved));
        }
    }

    private bool StillMatches(ValidatedKakaoContext context)
    {
        if (_native.GetClipboardSequenceNumber() !=
            context.ClipboardSequenceNumber)
        {
            return false;
        }

        var foreground = _native.GetForegroundWindow();
        var processId = _native.GetProcessId(foreground);
        var focused = _native.GetFocusedWindow(foreground);
        var current = new PasteTrigger(
            context.EventId,
            foreground,
            focused,
            processId,
            context.ClipboardSequenceNumber,
            context.OccurredAt,
            false);
        return _validator.TryValidate(current, out var validated) &&
               validated.ChatRootWindow == context.ChatRootWindow &&
               validated.InputWindow == context.InputWindow;
    }

    public async ValueTask DisposeAsync()
    {
        _hook.PasteDetected -= OnPasteDetected;
        _hook.Dispose();
        _triggers.Writer.TryComplete();
        _cancellation.Cancel();
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

        _clipboardReader.Dispose();
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }
}
