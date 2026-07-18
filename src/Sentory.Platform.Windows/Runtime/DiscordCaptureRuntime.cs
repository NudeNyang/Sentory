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
        _worker = Task.Run(() => ProcessTriggersAsync(_cancellation.Token));
        _hook.Start();
    }

    private void OnPasteDetected(object? sender, PasteTrigger trigger) =>
        _triggers.Writer.TryWrite(trigger);

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
        if (clipboard?.Text is null)
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
                cancellation);
            registration.Task = Task.Run(() => RunCandidateAsync(registration));
            _candidates.Add(registration);
        }
    }

    private async Task RunCandidateAsync(CandidateRegistration registration)
    {
        try
        {
            var context = registration.Context;
            var response = await _confirmationClient.ConfirmAsync(
                new DiscordConfirmationRequest(
                    context.MainWindow.ToInt64(),
                    context.RendererWindow.ToInt64(),
                    context.ProcessId,
                    registration.Urls.Select(url => url.Value).ToList()),
                registration.Cancellation.Token);
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
            var results = await _coordinator.CaptureUrlsAsync(
                context.EventId,
                string.Join('\n', registration.Urls.Select(url => url.Original)),
                SourceApp.Discord,
                CaptureMethod.DiscordConfirmedSend,
                DeliveryStatus.Confirmed,
                context.ContextHash,
                response.ConfirmedAt ?? DateTimeOffset.UtcNow,
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
                        response.ConfirmedAt ?? DateTimeOffset.UtcNow,
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

                _candidates.Remove(registration);
            }

            registration.Cancellation.Dispose();
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
                "Discord 입력 구조를 확인하지 못해 해당 링크를 저장하지 않았습니다.",
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
        CancellationTokenSource cancellation)
    {
        public ValidatedDiscordContext Context { get; } = context;

        public IReadOnlyList<NormalizedUrl> Urls { get; } = urls;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } =
            System.Threading.Tasks.Task.CompletedTask;
    }
}
