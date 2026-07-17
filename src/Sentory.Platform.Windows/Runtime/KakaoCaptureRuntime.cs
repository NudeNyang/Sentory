using System.Threading.Channels;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

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

    private async Task ProcessTriggersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var trigger in _triggers.Reader.ReadAllAsync(
                               cancellationToken))
            {
                if (_paused ||
                    !_validator.TryValidate(trigger, out var context))
                {
                    continue;
                }

                var clipboard = await _clipboardReader.ReadAsync(
                    context.ClipboardSequenceNumber,
                    cancellationToken);
                if (clipboard is null)
                {
                    continue;
                }

                if (clipboard.Image is not null)
                {
                    await CaptureImageIfConfirmedAsync(
                        context,
                        clipboard.Image,
                        cancellationToken);
                    continue;
                }

                if (clipboard.Text is null ||
                    UrlExtractor.Extract(clipboard.Text).Count == 0)
                {
                    continue;
                }

                await Task.Delay(150, cancellationToken);
                if (!StillMatches(context))
                {
                    continue;
                }

                var matchesInput =
                    await _inputVerifier.ContainsClipboardUrlsAsync(
                        context.InputWindow,
                        clipboard.Text,
                        TimeSpan.FromMilliseconds(750),
                        cancellationToken);
                if (!matchesInput)
                {
                    continue;
                }

                var results = await _coordinator.CaptureUrlsAsync(
                    context.EventId,
                    clipboard.Text,
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
                var applied = results.Count(result => result.EventApplied);
                if (applied > 0)
                {
                    Captured?.Invoke(
                        this,
                        new CaptureNotification(
                            ContentKind.Url,
                            applied,
                            context.OccurredAt));
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CaptureImageIfConfirmedAsync(
        ValidatedKakaoContext context,
        ClipboardImageSnapshot image,
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

        var result = await _coordinator.CaptureImageAsync(
            context.EventId,
            image.PngBytes,
            image.Sha256,
            image.PixelWidth,
            image.PixelHeight,
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
        if (result.EventApplied)
        {
            Captured?.Invoke(
                this,
                new CaptureNotification(
                    ContentKind.Image,
                    1,
                    context.OccurredAt));
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
