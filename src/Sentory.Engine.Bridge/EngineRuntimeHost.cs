using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;
using Sentory.Core;
using Sentory.Infrastructure.Data;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Engine.Bridge;

public sealed class EngineRuntimeHost : IAsyncDisposable
{
    private readonly SqliteCaptureRepository _repository;
    private readonly SentorySettingsStore _settingsStore;
    private readonly ConcurrentQueue<EngineRuntimeEventDto> _events = new();
    private readonly TaskCompletionSource _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _stateGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Thread _dispatcherThread;
    private CompositeCaptureRuntime? _runtime;
    private Dispatcher? _dispatcher;
    private IReadOnlyList<IDisposable> _dropRuntimes = [];
    private CaptureRuntimeState _discordState = CaptureRuntimeState.Connecting;
    private Task? _discordMonitor;
    private int? _observedDiscordProcessId;
    private bool _discordAccessibilityArgumentMissing;
    private string? _lastIssueCode;
    private string? _lastIssue;
    private bool _started;

    public EngineRuntimeHost(
        SqliteCaptureRepository repository,
        SentoryDataPaths paths)
    {
        _repository = repository;
        _settingsStore = new SentorySettingsStore(paths);
        _dispatcherThread = new Thread(RunDispatcher)
        {
            IsBackground = true,
            Name = "Sentory capture runtime"
        };
        _dispatcherThread.SetApartmentState(ApartmentState.STA);
    }

    public async Task StartAsync()
    {
        if (_started)
        {
            await _ready.Task;
            return;
        }
        _started = true;
        _dispatcherThread.Start();
        await _ready.Task;
        _discordMonitor = Task.Run(() => MonitorDiscordAsync(_lifetime.Token));
    }

    public EngineSettingsDto GetSettings()
    {
        var settings = _settingsStore.Load();
        return CreateSettingsDto(settings);
    }

    public async Task<EngineSettingsDto> UpdateSettingsAsync(
        EngineSettingsPatchDto patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var settings = _settingsStore.Load();
        if (patch.ThemeMode is { } themeMode)
        {
            settings.ThemeMode = themeMode;
        }
        if (patch.Language is { } language)
        {
            settings.Language = language;
            settings.LanguageSettingVersion = SentorySettings.CurrentLanguageSettingVersion;
        }
        ApplyIfPresent(patch.DiscordSupportEnabled, value => settings.DiscordSupportEnabled = value);
        ApplyIfPresent(patch.KakaoTalkSupportEnabled, value => settings.KakaoTalkSupportEnabled = value);
        ApplyIfPresent(patch.SlackSupportEnabled, value => settings.SlackSupportEnabled = value);
        ApplyIfPresent(patch.WhatsAppSupportEnabled, value => settings.WhatsAppSupportEnabled = value);
        ApplyIfPresent(patch.TelegramSupportEnabled, value => settings.TelegramSupportEnabled = value);
        ApplyIfPresent(patch.LineSupportEnabled, value => settings.LineSupportEnabled = value);
        ApplyIfPresent(patch.WeChatSupportEnabled, value => settings.WeChatSupportEnabled = value);
        ApplyIfPresent(patch.StartWithWindows, value => settings.StartWithWindows = value);
        ApplyIfPresent(patch.AutoFavoriteEnabled, value => settings.AutoFavoriteEnabled = value);
        if (patch.AutoFavoriteCopyThreshold is { } threshold)
        {
            settings.AutoFavoriteCopyThreshold = threshold;
            settings.AutoFavoriteChangedAt = DateTimeOffset.UtcNow;
        }
        if (patch.AutoCleanupDays is { } days)
        {
            settings.AutoCleanupDays = days;
            settings.LastAutoCleanupAt = null;
        }
        _settingsStore.Save(settings);
        _repository.ConfigureAutomaticFavorites(
            settings.AutoFavoriteEnabled,
            settings.AutoFavoriteCopyThreshold);
        await ApplySourceSettingsAsync(settings);
        Enqueue("settings-changed", CreateSettingsDto(settings));
        return CreateSettingsDto(settings);
    }

    public EngineRuntimePollDto Poll()
    {
        var pending = new List<EngineRuntimeEventDto>();
        while (pending.Count < 100 && _events.TryDequeue(out var item))
        {
            pending.Add(item);
        }
        return new EngineRuntimePollDto(GetStatus(), pending);
    }

    public async Task<EngineRuntimeStatusDto> RepairDiscordAsync()
    {
        lock (_stateGate)
        {
            _discordState = CaptureRuntimeState.Recovering;
            _discordAccessibilityArgumentMissing = false;
            _lastIssueCode = null;
            _lastIssue = null;
        }
        Enqueue("detection-status", GetStatus());
        try
        {
            await new DiscordAccessibilityLauncher().RestartAsync();
            var dispatcher = _dispatcher ?? throw new InvalidOperationException(
                "감지 런타임이 준비되지 않았습니다.");
            await dispatcher.InvokeAsync(() =>
                _runtime?.RequestRecovery(SourceApp.Discord));
            lock (_stateGate)
            {
                _discordState = CaptureRuntimeState.Connecting;
            }
        }
        catch (Exception exception)
        {
            lock (_stateGate)
            {
                _discordState = CaptureRuntimeState.ReconnectRequired;
                _lastIssueCode = "discord-repair-failed";
                _lastIssue = exception.Message;
            }
            Enqueue("runtime-issue", new
            {
                code = "discord-repair-failed",
                message = exception.Message
            });
            throw;
        }
        var status = GetStatus();
        Enqueue("detection-status", status);
        return status;
    }

    private void RunDispatcher()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            var acceptInjectedInput = string.Equals(
                Environment.GetEnvironmentVariable("SENTORY_ACCEPT_INJECTED_INPUT"),
                "1",
                StringComparison.Ordinal);
            Action<string, string> diagnostic = (category, message) =>
                Console.Error.WriteLine(
                    $"{category}\t{message.Replace('\r', ' ').Replace('\n', ' ')}");
            var kakao = new KakaoCaptureRuntime(_repository, acceptInjectedInput);
            var discord = new DiscordCaptureRuntime(_repository, acceptInjectedInput);
            var slack = new SlackCaptureRuntime(_repository, acceptInjectedInput, diagnostic);
            var whatsApp = new WhatsAppCaptureRuntime(_repository, acceptInjectedInput, diagnostic);
            var telegram = new TelegramCaptureRuntime(_repository, acceptInjectedInput, diagnostic);
            var line = new LineCaptureRuntime(_repository, acceptInjectedInput, diagnostic);
            var weChat = new WeChatCaptureRuntime(_repository, acceptInjectedInput, diagnostic);
            _runtime = new CompositeCaptureRuntime(
                (SourceApp.KakaoTalk, kakao),
                (SourceApp.Discord, discord),
                (SourceApp.Slack, slack),
                (SourceApp.WhatsApp, whatsApp),
                (SourceApp.Telegram, telegram),
                (SourceApp.Line, line),
                (SourceApp.WeChat, weChat));
            _dropRuntimes =
            [
                new KakaoDropOverlayRuntime(kakao, () => false, () => "사진 놓기", () => "사진을 놓아 입력합니다.", diagnostic),
                new DiscordDropOverlayRuntime(discord, diagnostic),
                new SlackDropOverlayRuntime(slack, diagnostic),
                new WhatsAppDropOverlayRuntime(whatsApp, diagnostic),
                new TelegramDropOverlayRuntime(telegram, diagnostic),
                new LineDropOverlayRuntime(line, diagnostic),
                new WeChatDropOverlayRuntime(weChat, diagnostic)
            ];
            _runtime.Captured += OnCaptured;
            _runtime.IssueDetected += OnIssueDetected;
            _runtime.StatusChanged += OnStatusChanged;
            ApplySourceSettings(_settingsStore.Load());
            _runtime.Start();
            foreach (var dropRuntime in _dropRuntimes)
            {
                StartDropRuntime(dropRuntime);
            }
            Enqueue("detection-status", GetStatus());
            _ready.TrySetResult();
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            Console.Error.WriteLine(exception);
        }
    }

    private async Task ApplySourceSettingsAsync(SentorySettings settings)
    {
        var dispatcher = _dispatcher ?? throw new InvalidOperationException(
            "감지 런타임이 준비되지 않았습니다.");
        await dispatcher.InvokeAsync(() => ApplySourceSettings(settings));
    }

    private void ApplySourceSettings(SentorySettings settings)
    {
        if (_runtime is null)
        {
            return;
        }
        _runtime.SetSourceEnabled(SourceApp.Discord, settings.DiscordSupportEnabled);
        _runtime.SetSourceEnabled(SourceApp.KakaoTalk, settings.KakaoTalkSupportEnabled);
        _runtime.SetSourceEnabled(SourceApp.Slack, settings.SlackSupportEnabled);
        _runtime.SetSourceEnabled(SourceApp.WhatsApp, settings.WhatsAppSupportEnabled);
        _runtime.SetSourceEnabled(SourceApp.Telegram, settings.TelegramSupportEnabled);
        _runtime.SetSourceEnabled(SourceApp.Line, settings.LineSupportEnabled);
        _runtime.SetSourceEnabled(SourceApp.WeChat, settings.WeChatSupportEnabled);
    }

    private void OnCaptured(object? sender, CaptureNotification notification)
    {
        lock (_stateGate)
        {
            _lastIssueCode = null;
            _lastIssue = null;
        }
        Enqueue("captured", new EngineCaptureEventDto(
            notification.Kind.ToString(),
            notification.Count,
            notification.CapturedAt,
            notification.SourceApp?.ToString(),
            notification.DeliveryStatus?.ToString()));
    }

    private void OnIssueDetected(object? sender, CaptureRuntimeIssue issue)
    {
        lock (_stateGate)
        {
            _lastIssueCode = issue.Code;
            _lastIssue = issue.UserMessage;
        }
        Enqueue("runtime-issue", new
        {
            code = issue.Code,
            message = issue.UserMessage,
            occurredAt = issue.OccurredAt
        });
    }

    private void OnStatusChanged(object? sender, CaptureRuntimeStatus status)
    {
        if (status.SourceApp == SourceApp.Discord)
        {
            lock (_stateGate)
            {
                _discordState = _discordAccessibilityArgumentMissing &&
                    status.State != CaptureRuntimeState.Ready
                        ? CaptureRuntimeState.ReconnectRequired
                        : status.State;
                if (status.State == CaptureRuntimeState.Ready)
                {
                    _lastIssueCode = null;
                    _lastIssue = null;
                }
            }
        }
        Enqueue("detection-status", GetStatus());
    }

    private EngineRuntimeStatusDto GetStatus()
    {
        CaptureRuntimeState discordState;
        string? issueCode;
        string? issue;
        lock (_stateGate)
        {
            discordState = _discordState;
            issueCode = _lastIssueCode;
            issue = _lastIssue;
        }
        var settings = _settingsStore.Load();
        return new EngineRuntimeStatusDto(
            discordState.ToString(),
            new DiscordAccessibilityLauncher().IsRunning(),
            issueCode,
            issue,
            CreateSourceStates(settings));
    }

    private async Task MonitorDiscordAsync(CancellationToken cancellationToken)
    {
        var launcher = new DiscordAccessibilityLauncher();
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settings = _settingsStore.Load();
                var processId = settings.DiscordSupportEnabled
                    ? launcher.GetMainProcessId()
                    : null;
                if (processId != _observedDiscordProcessId)
                {
                    _observedDiscordProcessId = processId;
                    var argumentState = processId is int currentProcessId
                        ? await Task.Run(
                            () => launcher.GetAccessibilityArgumentState(currentProcessId),
                            cancellationToken)
                        : DiscordAccessibilityArgumentState.Unknown;
                    var argumentMissing =
                        argumentState == DiscordAccessibilityArgumentState.Missing;
                    lock (_stateGate)
                    {
                        _discordAccessibilityArgumentMissing = argumentMissing;
                        if (argumentMissing)
                        {
                            _discordState = CaptureRuntimeState.ReconnectRequired;
                            _lastIssueCode = "discord-accessibility-restart-required";
                            _lastIssue = "Discord를 재시작해야 감지를 연결할 수 있습니다.";
                        }
                        else if (!processId.HasValue)
                        {
                            _discordState = CaptureRuntimeState.Connecting;
                            _lastIssueCode = null;
                            _lastIssue = null;
                        }
                    }
                    if (processId.HasValue && !argumentMissing &&
                        _dispatcher is { } dispatcher)
                    {
                        await dispatcher.InvokeAsync(() =>
                            _runtime?.RequestRecovery(SourceApp.Discord));
                    }
                    Enqueue("detection-status", GetStatus());
                }
                await Task.Delay(
                    settings.DiscordSupportEnabled
                        ? TimeSpan.FromSeconds(3)
                        : TimeSpan.FromSeconds(30),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private EngineSettingsDto CreateSettingsDto(SentorySettings settings) => new(
        settings.GetThemeMode().ToString(),
        settings.Language,
        settings.StartWithWindows ?? false,
        settings.AutoFavoriteEnabled,
        settings.AutoFavoriteCopyThreshold,
        settings.AutoCleanupDays,
        CreateSourceStates(settings));

    private static IReadOnlyDictionary<string, bool> CreateSourceStates(
        SentorySettings settings) => new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [SourceApp.Discord.ToString()] = settings.DiscordSupportEnabled,
            [SourceApp.KakaoTalk.ToString()] = settings.KakaoTalkSupportEnabled,
            [SourceApp.Slack.ToString()] = settings.SlackSupportEnabled,
            [SourceApp.WhatsApp.ToString()] = settings.WhatsAppSupportEnabled,
            [SourceApp.Telegram.ToString()] = settings.TelegramSupportEnabled,
            [SourceApp.Line.ToString()] = settings.LineSupportEnabled,
            [SourceApp.WeChat.ToString()] = settings.WeChatSupportEnabled
        };

    private void Enqueue(string type, object payload) =>
        _events.Enqueue(new EngineRuntimeEventDto(type, payload));

    private static void ApplyIfPresent(bool? value, Action<bool> apply)
    {
        if (value is { } present)
        {
            apply(present);
        }
    }

    private static void StartDropRuntime(IDisposable runtime)
    {
        switch (runtime)
        {
            case KakaoDropOverlayRuntime value: value.Start(); break;
            case DiscordDropOverlayRuntime value: value.Start(); break;
            case SlackDropOverlayRuntime value: value.Start(); break;
            case WhatsAppDropOverlayRuntime value: value.Start(); break;
            case TelegramDropOverlayRuntime value: value.Start(); break;
            case LineDropOverlayRuntime value: value.Start(); break;
            case WeChatDropOverlayRuntime value: value.Start(); break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started)
        {
            return;
        }
        _lifetime.Cancel();
        if (_discordMonitor is not null)
        {
            try
            {
                await _discordMonitor;
            }
            catch (OperationCanceledException)
            {
            }
        }
        try
        {
            await _ready.Task;
        }
        catch
        {
            return;
        }
        if (_dispatcher is { } dispatcher)
        {
            CompositeCaptureRuntime? runtimeToDispose = null;
            await dispatcher.InvokeAsync(() =>
            {
                foreach (var dropRuntime in _dropRuntimes)
                {
                    dropRuntime.Dispose();
                }
                _dropRuntimes = [];
                if (_runtime is not null)
                {
                    _runtime.Captured -= OnCaptured;
                    _runtime.IssueDetected -= OnIssueDetected;
                    _runtime.StatusChanged -= OnStatusChanged;
                    runtimeToDispose = _runtime;
                    _runtime = null;
                }
            });
            if (runtimeToDispose is not null)
            {
                await runtimeToDispose.DisposeAsync();
            }
            await dispatcher.InvokeAsync(() =>
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            });
        }
        if (_dispatcherThread.IsAlive)
        {
            _dispatcherThread.Join(TimeSpan.FromSeconds(5));
        }
        _lifetime.Dispose();
    }
}

public sealed record EngineSettingsDto(
    string ThemeMode,
    string Language,
    bool StartWithWindows,
    bool AutoFavoriteEnabled,
    int AutoFavoriteCopyThreshold,
    int AutoCleanupDays,
    IReadOnlyDictionary<string, bool> Sources);

public sealed record EngineSettingsPatchDto(
    string? ThemeMode = null,
    string? Language = null,
    bool? DiscordSupportEnabled = null,
    bool? KakaoTalkSupportEnabled = null,
    bool? SlackSupportEnabled = null,
    bool? WhatsAppSupportEnabled = null,
    bool? TelegramSupportEnabled = null,
    bool? LineSupportEnabled = null,
    bool? WeChatSupportEnabled = null,
    bool? StartWithWindows = null,
    bool? AutoFavoriteEnabled = null,
    int? AutoFavoriteCopyThreshold = null,
    int? AutoCleanupDays = null);

public sealed record EngineRuntimeStatusDto(
    string DiscordState,
    bool DiscordRunning,
    string? LastIssueCode,
    string? LastIssue,
    IReadOnlyDictionary<string, bool> Sources);

public sealed record EngineRuntimeEventDto(string Type, object Payload);

public sealed record EngineRuntimePollDto(
    EngineRuntimeStatusDto Status,
    IReadOnlyList<EngineRuntimeEventDto> Events);

public sealed record EngineCaptureEventDto(
    string Kind,
    int Count,
    DateTimeOffset CapturedAt,
    string? SourceApp,
    string? DeliveryStatus);
