using System.Threading.Channels;
using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

public sealed class DiscordCaptureRuntime : ICaptureRuntime
{
    private const int MaximumActiveCandidates = 8;

    private readonly INativeWindowApi _native;
    private readonly DiscordContextValidator _validator;
    private readonly LowLevelPasteHook _hook;
    private readonly StaClipboardReader _clipboardReader;
    private readonly IDiscordConfirmationClient _confirmationClient;
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
    private readonly List<CandidateRegistration> _candidates = [];
    private readonly HashSet<string> _activeUrls =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeImageHashes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _recentSendSignals =
        new(StringComparer.Ordinal);
    private Task? _worker;
    private volatile bool _paused;
    private DateTimeOffset _lastIssueReportedAt = DateTimeOffset.MinValue;

    public DiscordCaptureRuntime(
        ICaptureRepository repository,
        bool acceptInjectedInput = false)
    {
        var native = new NativeWindowApi();
        _native = native;
        _validator = new DiscordContextValidator(native, native);
        _hook = new LowLevelPasteHook(native, acceptInjectedInput);
        _clipboardReader = new StaClipboardReader(native);
        _confirmationClient = new DiscordWorkerClient();
        _coordinator = new CaptureCoordinator(repository);
    }

    public event EventHandler<CaptureNotification>? Captured;

    public event EventHandler<CaptureRuntimeIssue>? IssueDetected;

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

        var clipboard = await _clipboardReader.ReadAsync(
            context.ClipboardSequenceNumber,
            cancellationToken);
        if (clipboard is null)
        {
            DiscordCaptureTrace.Write("clipboard-read-empty");
            return;
        }

        if (clipboard.Image is not null)
        {
            DiscordCaptureTrace.Write(
                "clipboard-image-read",
                $"width={clipboard.Image.PixelWidth} height={clipboard.Image.PixelHeight} bytes={clipboard.Image.PngBytes.Length}");
            StartImageCandidate(context, clipboard.Image);
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
            var context = registration.Context;
            using var baselineCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    registration.Cancellation.Token);
            var baselineTask = ConfirmAsync(
                registration,
                explicitSendObserved: false,
                baselineCancellation.Token);
            var completed = await Task.WhenAny(
                baselineTask,
                registration.SendObserved.Task);

            DiscordConfirmationResponse response;
            if (completed == registration.SendObserved.Task ||
                (registration.SendObserved.Task.IsCompletedSuccessfully &&
                 baselineTask.IsCompletedSuccessfully &&
                 baselineTask.Result.Outcome !=
                 DiscordConfirmationOutcome.Confirmed))
            {
                baselineCancellation.Cancel();
                try
                {
                    await baselineTask;
                }
                catch (OperationCanceledException)
                {
                }

                await Task.Delay(500, registration.Cancellation.Token);
                response = await ConfirmAsync(
                    registration,
                    explicitSendObserved: true,
                    registration.Cancellation.Token);
            }
            else
            {
                response = await baselineTask;
            }

            DiscordCaptureTrace.Write(
                registration.Image is null
                    ? "url-confirmation-response"
                    : "image-confirmation-response",
                $"outcome={response.Outcome} signals={string.Join(',', response.ConfirmationSignals)}");
            if (response.Outcome ==
                DiscordConfirmationOutcome.DetectionUnavailable)
            {
                ReportDetectionUnavailable();
                return;
            }

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
        if (_recentSendSignals.TryGetValue(
                registration.Context.ContextHash,
                out var sentAt) &&
            sentAt >= registration.Context.OccurredAt &&
            DateTimeOffset.UtcNow - sentAt <= TimeSpan.FromSeconds(3))
        {
            registration.SendObserved.TrySetResult(sentAt);
        }

        foreach (var expired in _recentSendSignals
                     .Where(pair =>
                         DateTimeOffset.UtcNow - pair.Value >
                         TimeSpan.FromSeconds(10))
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
            context.EventId,
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
            context.EventId,
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
        _hook.PasteDetected -= OnPasteDetected;
        _hook.SendDetected -= OnSendDetected;
        _hook.Dispose();
        _triggers.Writer.TryComplete();
        _cancellation.Cancel();
        CancelActiveCandidates();
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
        IReadOnlyList<NormalizedUrl> urls,
        ClipboardImageSnapshot? image,
        CancellationTokenSource cancellation)
    {
        public ValidatedDiscordContext Context { get; } = context;

        public IReadOnlyList<NormalizedUrl> Urls { get; } = urls;

        public ClipboardImageSnapshot? Image { get; } = image;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } =
            System.Threading.Tasks.Task.CompletedTask;

        public TaskCompletionSource<DateTimeOffset> SendObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
