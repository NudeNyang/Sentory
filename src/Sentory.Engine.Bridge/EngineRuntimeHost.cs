using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Links;
using Sentory.Infrastructure.Ocr;
using Sentory.Infrastructure.Sync;
using Sentory.Infrastructure.Updates;
using Sentory.Platform.Windows.Ocr;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Engine.Bridge;

public sealed class EngineRuntimeHost : IAsyncDisposable
{
    private const string CurrentVersion = "2.0.7";
    private readonly SqliteCaptureRepository _repository;
    private readonly SentoryDataPaths _paths;
    private readonly SentorySettingsStore _settingsStore;
    private readonly AutomaticCleanupCoordinator _automaticCleanup;
    private readonly Func<int, DateTimeOffset, CancellationToken, Task<int>>
        _enrichLinkPreviews;
    private readonly Func<int, CancellationToken, Task<OcrEnrichmentBatchResult>>?
        _enrichImageOcr;
    private readonly LinkPreviewFetcher? _linkPreviewFetcher;
    private readonly PaddleOcrImageTextRecognizer? _ocrRecognizer;
    private readonly SemaphoreSlim _linkPreviewWakeSignal = new(0, 1);
    private readonly SemaphoreSlim _ocrWakeSignal = new(0, 1);
    private readonly SemaphoreSlim _syncWakeSignal = new(0, 1);
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);
    private readonly SyncRuntimeStatusTracker _syncStatusTracker = new();
    private readonly ConcurrentQueue<EngineRuntimeEventDto> _events = new();
    private readonly TaskCompletionSource _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _stateGate = new();
    private readonly object _updateStateGate = new();
    private readonly object _webDavStoreGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Thread _dispatcherThread;
    private CompositeCaptureRuntime? _runtime;
    private Dispatcher? _dispatcher;
    private IReadOnlyList<IDisposable> _dropRuntimes = [];
    private CaptureRuntimeState _discordState = CaptureRuntimeState.Connecting;
    private Task? _discordMonitor;
    private Task? _maintenanceTask;
    private Task? _linkPreviewTask;
    private Task? _ocrTask;
    private Task? _syncTask;
    private Task? _updateDownloadTask;
    private LocalFolderSyncRuntimeService? _syncRuntimeService;
    private WebDavSyncObjectStore? _webDavStore;
    private string? _webDavStoreConfiguration;
    private int? _observedDiscordProcessId;
    private bool _discordAccessibilityArgumentMissing;
    private string? _lastIssueCode;
    private string? _lastIssue;
    private bool _started;
    private int _disposed;
    private ReleaseUpdate? _availableUpdate;
    private string? _downloadedUpdatePackage;

    public EngineRuntimeHost(
        SqliteCaptureRepository repository,
        SentoryDataPaths paths)
        : this(repository, paths, null, null)
    {
    }

    internal EngineRuntimeHost(
        SqliteCaptureRepository repository,
        SentoryDataPaths paths,
        Func<int, DateTimeOffset, CancellationToken, Task<int>>?
            enrichLinkPreviews,
        Func<int, CancellationToken, Task<OcrEnrichmentBatchResult>>?
            enrichImageOcr = null)
    {
        _repository = repository;
        _paths = paths;
        _settingsStore = new SentorySettingsStore(paths);
        _automaticCleanup = new AutomaticCleanupCoordinator(
            repository,
            _settingsStore);
        if (enrichLinkPreviews is null)
        {
            _linkPreviewFetcher = new LinkPreviewFetcher(paths);
            var linkPreviewService = new LinkPreviewEnrichmentService(
                repository,
                _linkPreviewFetcher);
            _enrichLinkPreviews = linkPreviewService.EnrichBatchAsync;
        }
        else
        {
            _enrichLinkPreviews = enrichLinkPreviews;
        }
        if (enrichImageOcr is not null)
        {
            _enrichImageOcr = enrichImageOcr;
        }
        else if (!string.Equals(
                     Environment.GetEnvironmentVariable(
                         "SENTORY_DISABLE_OCR"),
                     "1",
                     StringComparison.Ordinal))
        {
            _ocrRecognizer = new PaddleOcrImageTextRecognizer(
                paths.OcrModelsDirectory,
                new WindowsImageTextRecognizer());
            var ocrService = new OcrEnrichmentService(
                repository,
                _ocrRecognizer,
                paths,
                (sha256, exception) => Console.Error.WriteLine(
                    $"image-ocr-item-failed " +
                    $"{sha256[..Math.Min(12, sha256.Length)]} " +
                    $"{exception.GetType().Name}: {exception.Message}"),
                new WindowsImageMetadataTitleReader());
            _enrichImageOcr = ocrService.EnrichBatchAsync;
        }
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
        _maintenanceTask = Task.Run(() => RunMaintenanceAsync(_lifetime.Token));
        _linkPreviewTask = Task.Run(() =>
            RunLinkPreviewLoopAsync(_lifetime.Token));
        if (_enrichImageOcr is not null)
        {
            _ocrTask = Task.Run(() => RunOcrLoopAsync(_lifetime.Token));
        }
        _syncTask = Task.Run(() => RunSyncLoopAsync(_lifetime.Token));
    }

    public EngineSettingsDto GetSettings()
    {
        var settings = _settingsStore.Load();
        return CreateSettingsDto(settings);
    }

    public EngineStartupPreferenceDto GetStartupPreference()
    {
        var settingsFileExisted = File.Exists(_paths.SettingsPath);
        var settings = _settingsStore.Load();
        return new EngineStartupPreferenceDto(
            settingsFileExisted,
            settings.StartWithWindows);
    }

    public IReadOnlyList<EngineSyncFolderCandidateDto>
        DiscoverSyncFolders() =>
        WindowsCloudSyncFolderDiscovery.Discover()
            .Select(candidate => new EngineSyncFolderCandidateDto(
                candidate.ProviderId,
                candidate.ProviderName,
                candidate.FolderPath,
                candidate.DisplayName))
            .ToArray();

    public async Task<EngineSettingsDto> ConfigureSyncFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var selectedPath = Path.GetFullPath(folderPath);
        Directory.CreateDirectory(selectedPath);
        var capability = await SyncFolderCapabilityProbe.CheckAsync(
            selectedPath,
            cancellationToken);
        if (!capability.IsSupported)
        {
            throw new IOException(capability.FailureReason switch
            {
                SyncFolderCapabilityFailure.NotDirectory =>
                    "파일은 동기화 위치로 사용할 수 없습니다.",
                SyncFolderCapabilityFailure.RenameUnavailable =>
                    "이 위치에서는 파일 이름 변경을 사용할 수 없습니다.",
                SyncFolderCapabilityFailure.ContentMismatch =>
                    "이 위치에 쓴 파일의 내용이 달라졌습니다.",
                _ => "이 위치에서 파일을 만들거나 읽을 수 없습니다."
            });
        }

        var settings = _settingsStore.Load();
        var folderChanged =
            settings.SyncProvider != SentorySettings.FolderSyncProvider ||
            !string.Equals(
                settings.SyncFolderPath,
                selectedPath,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        var previous = CaptureSyncConfiguration(settings);
        settings.SyncProvider = SentorySettings.FolderSyncProvider;
        settings.SyncFolderPath = selectedPath;
        settings.SyncEnabled = true;
        if (folderChanged ||
            !SyncDeviceIdentity.IsValid(settings.SyncDeviceId))
        {
            settings.SyncDeviceId = SyncDeviceIdentity.Create();
            settings.SyncStorageVersion =
                SentorySettings.CurrentSyncStorageVersion;
            settings.SyncMigrationDeviceId = null;
            settings.SyncStoreId = null;
        }

        _settingsStore.Save(settings);
        try
        {
            if (folderChanged)
            {
                await EnsureSyncJournalInitializedAsync(
                    previous.DeviceId,
                    settings.SyncDeviceId!,
                    cancellationToken);
                await SqliteSyncOperationJournal.ResetForNewStoreAsync(
                    _paths,
                    settings.SyncDeviceId!,
                    cancellationToken);
            }
        }
        catch
        {
            RestoreSyncConfiguration(settings, previous);
            _settingsStore.Save(settings);
            throw;
        }

        PublishSyncStatus(SyncRuntimeState.Waiting);
        WakeSyncWorker();
        var result = CreateSettingsDto(_settingsStore.Load());
        Enqueue("settings-changed", result);
        return result;
    }

    public async Task<EngineSettingsDto> ConfigureSyncWebDavAsync(
        string endpoint,
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Load();
        var normalizedUsername = string.IsNullOrWhiteSpace(username)
            ? null
            : username.Trim();
        using var candidate = new WebDavSyncObjectStore(
            endpoint,
            normalizedUsername,
            ResolveWebDavPassword(
                settings,
                endpoint,
                normalizedUsername,
                password));
        await candidate.ProbeAsync(cancellationToken);
        var storeId = await GetOrCreateWebDavStoreIdAsync(
            candidate,
            cancellationToken);
        var normalizedEndpoint = candidate.Endpoint.AbsoluteUri;
        var configurationChanged =
            settings.SyncProvider != SentorySettings.WebDavSyncProvider ||
            !string.Equals(
                settings.SyncWebDavEndpoint,
                normalizedEndpoint,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                settings.SyncWebDavUsername,
                normalizedUsername,
                StringComparison.Ordinal) ||
            !string.Equals(
                settings.SyncStoreId,
                storeId,
                StringComparison.Ordinal);
        var previous = CaptureSyncConfiguration(settings);
        settings.SyncProvider = SentorySettings.WebDavSyncProvider;
        settings.SyncFolderPath = null;
        settings.SyncWebDavEndpoint = normalizedEndpoint;
        settings.SyncWebDavUsername = normalizedUsername;
        if (password is not null)
        {
            settings.SyncWebDavProtectedPassword =
                WebDavCredentialProtector.Protect(password);
        }
        else if (configurationChanged)
        {
            settings.SyncWebDavProtectedPassword = null;
        }
        settings.SyncEnabled = true;
        settings.SyncStorageVersion = SentorySettings.CurrentSyncStorageVersion;
        settings.SyncMigrationDeviceId = null;
        settings.SyncStoreId = storeId;
        if (configurationChanged ||
            !SyncDeviceIdentity.IsValid(settings.SyncDeviceId))
        {
            settings.SyncDeviceId = SyncDeviceIdentity.Create();
        }

        _settingsStore.Save(settings);
        try
        {
            if (configurationChanged)
            {
                await EnsureSyncJournalInitializedAsync(
                    previous.DeviceId,
                    settings.SyncDeviceId!,
                    cancellationToken);
                await SqliteSyncOperationJournal.ResetForNewStoreAsync(
                    _paths,
                    settings.SyncDeviceId!,
                    cancellationToken);
            }
        }
        catch
        {
            RestoreSyncConfiguration(settings, previous);
            _settingsStore.Save(settings);
            throw;
        }

        PublishSyncStatus(SyncRuntimeState.Waiting);
        WakeSyncWorker();
        var result = CreateSettingsDto(_settingsStore.Load());
        Enqueue("settings-changed", result);
        return result;
    }

    public async Task<EngineSettingsDto> ToggleSyncAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Load();
        if (enabled && !HasCompleteSyncConfiguration(settings))
        {
            throw new InvalidOperationException(
                "먼저 클라우드 폴더나 NAS WebDAV 연결을 설정해 주세요.");
        }

        if (enabled && !SyncDeviceIdentity.IsValid(settings.SyncDeviceId))
        {
            settings.SyncDeviceId = SyncDeviceIdentity.Create();
            await EnsureSyncJournalInitializedAsync(
                null,
                settings.SyncDeviceId,
                cancellationToken);
            await SqliteSyncOperationJournal.ResetForNewStoreAsync(
                _paths,
                settings.SyncDeviceId,
                cancellationToken);
        }
        settings.SyncEnabled = enabled;
        _settingsStore.Save(settings);
        PublishSyncStatus(
            enabled ? SyncRuntimeState.Waiting : SyncRuntimeState.Disabled);
        if (enabled)
        {
            WakeSyncWorker();
        }

        var result = CreateSettingsDto(_settingsStore.Load());
        Enqueue("settings-changed", result);
        return result;
    }

    public async Task<DataStatistics> GetDataStatisticsAsync(
        CancellationToken cancellationToken = default) =>
        await _repository.GetDataStatisticsAsync(cancellationToken);

    public async Task<DataCleanupPreview> PreviewCleanupAsync(
        CancellationToken cancellationToken = default) =>
        await _repository.PreviewCleanupAsync(null, cancellationToken);

    public async Task<DataCleanupResult> CleanupAsync(
        CancellationToken cancellationToken = default) =>
        await _repository.CleanupAsync(null, cancellationToken);

    public string GetDataDirectory() => _paths.RootDirectory;

    public async Task<EngineUpdateCheckDto> CheckForUpdatesAsync(
        bool manual,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(
                    "SENTORY_DISTRIBUTION_CHANNEL"),
                "microsoft-store",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Microsoft Store 배포판에서는 앱 내 업데이트 확인을 제공하지 않습니다.");
        }

        await _updateCheckGate.WaitAsync(cancellationToken);
        try
        {
            lock (_updateStateGate)
            {
                if (_availableUpdate is { } readyUpdate &&
                    _downloadedUpdatePackage is { } readyPackage &&
                    File.Exists(readyPackage))
                {
                    return ReadyUpdate(readyUpdate);
                }

                if (_availableUpdate is { } downloadingUpdate &&
                    _updateDownloadTask is { IsCompleted: false })
                {
                    return PendingUpdate(downloadingUpdate);
                }
            }

            var settings = _settingsStore.Load();
            var now = DateTimeOffset.UtcNow;
            if (!UpdateCheckSchedule.ShouldCheck(
                    settings.LastUpdateCheckAt,
                    now,
                    manual))
            {
                return new EngineUpdateCheckDto(
                    Checked: false,
                    UpdateAvailable: false,
                    ReadyToInstall: false,
                    Version: null,
                    ReleasePage: null,
                    PackageKind: null);
            }

            settings.LastUpdateCheckAt = now;
            _settingsStore.Save(settings);
            var packageKind = UpdatePackageKindDetector.Resolve(
                AppContext.BaseDirectory);

            using var client = new GitHubReleaseUpdateClient();
            var update = await client.CheckAsync(
                CurrentVersion,
                RuntimeInformation.ProcessArchitecture,
                packageKind,
                cancellationToken);
            if (update is null)
            {
                return new EngineUpdateCheckDto(
                    Checked: true,
                    UpdateAvailable: false,
                    ReadyToInstall: false,
                    Version: null,
                    ReleasePage: null,
                    PackageKind: null);
            }

            settings.LastUpdateCheckAt = null;
            _settingsStore.Save(settings);
            lock (_updateStateGate)
            {
                _availableUpdate = update;
                _downloadedUpdatePackage = null;
                _updateDownloadTask = Task.Run(
                    () => DownloadUpdateAsync(update, _lifetime.Token),
                    _lifetime.Token);
            }
            return PendingUpdate(update);
        }
        catch
        {
            var settings = _settingsStore.Load();
            settings.LastUpdateCheckAt = null;
            _settingsStore.Save(settings);
            throw;
        }
        finally
        {
            _updateCheckGate.Release();
        }
    }

    public EngineUpdateInstallDto InstallPreparedUpdate(int hostProcessId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostProcessId);
        ReleaseUpdate update;
        string? package;
        lock (_updateStateGate)
        {
            update = _availableUpdate ?? throw new InvalidOperationException(
                "설치할 업데이트가 준비되지 않았습니다.");
            package = _downloadedUpdatePackage;
        }
        if (string.IsNullOrWhiteSpace(package) || !File.Exists(package))
        {
            throw new FileNotFoundException(
                "다운로드한 업데이트 패키지를 찾지 못했습니다.",
                package);
        }

        _ = UpdateApplier.PrepareAndLaunch(
            package,
            update.PackageKind,
            hostProcessId);
        return new EngineUpdateInstallDto(true, update.Version);
    }

    private async Task DownloadUpdateAsync(
        ReleaseUpdate update,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Sentory",
                "downloads",
                update.Version);
            using var client = new GitHubReleaseUpdateClient();
            var package = await client.DownloadAsync(
                update,
                directory,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_updateStateGate)
            {
                _downloadedUpdatePackage = package;
            }
            Enqueue("update-ready", ReadyUpdate(update));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_updateStateGate)
            {
                _availableUpdate = null;
                _downloadedUpdatePackage = null;
            }
            Enqueue("update-failed", new { message = exception.Message });
        }
    }

    private static EngineUpdateCheckDto ReadyUpdate(ReleaseUpdate update) =>
        new(
            Checked: true,
            UpdateAvailable: true,
            ReadyToInstall: true,
            Version: update.Version,
            ReleasePage: update.ReleasePage.AbsoluteUri,
            PackageKind: update.PackageKind == UpdatePackageKind.Installer
                ? "installer"
                : "portable");

    private static EngineUpdateCheckDto PendingUpdate(ReleaseUpdate update) =>
        new(
            Checked: true,
            UpdateAvailable: true,
            ReadyToInstall: false,
            Version: update.Version,
            ReleasePage: update.ReleasePage.AbsoluteUri,
            PackageKind: update.PackageKind == UpdatePackageKind.Installer
                ? "installer"
                : "portable");

    public async Task<EngineRuntimeStatusDto> TogglePauseAsync()
    {
        var dispatcher = _dispatcher ?? throw new InvalidOperationException(
            "감지 런타임이 준비되지 않았습니다.");
        await dispatcher.InvokeAsync(() =>
        {
            if (_runtime is not null)
            {
                _runtime.IsPaused = !_runtime.IsPaused;
            }
        });
        var status = GetStatus();
        Enqueue("detection-status", status);
        return status;
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
        ApplyIfPresent(
            patch.MessengerDetectionSetupCompleted,
            value => settings.MessengerDetectionSetupCompleted = value);
        ApplyIfPresent(
            patch.DiscordAutoRestartConsentGranted,
            value => settings.DiscordAutoRestartConsentGranted = value);
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
        WakeSyncWorker();
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

    public async Task<EngineRuntimeStatusDto> RepairDiscordAsync(
        int? expectedProcessId = null)
    {
        var launcher = new DiscordAccessibilityLauncher();
        if (expectedProcessId.HasValue &&
            launcher.GetMainProcessId() != expectedProcessId)
        {
            throw new InvalidOperationException(
                "Discord가 카운트다운 도중 다시 실행되어 자동 재시작을 취소했습니다.");
        }
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
            await launcher.RestartAsync();
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
            var startupSettings = _settingsStore.Load();
            _repository.ConfigureAutomaticFavorites(
                startupSettings.AutoFavoriteEnabled,
                startupSettings.AutoFavoriteCopyThreshold);
            ApplySourceSettings(startupSettings);
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
        if (notification.Kind is ContentKind.Url or ContentKind.Collection)
        {
            WakeLinkPreviewWorker();
        }
        if (notification.Kind is ContentKind.Image or ContentKind.Collection)
        {
            WakeOcrWorker();
        }
        WakeSyncWorker();
    }

    private void OnIssueDetected(object? sender, CaptureRuntimeIssue issue)
    {
        bool requiresDiscordRestart;
        lock (_stateGate)
        {
            _lastIssueCode = issue.Code;
            _lastIssue = issue.UserMessage;
            requiresDiscordRestart =
                issue.Code == "discord-detection-unavailable" &&
                _discordState == CaptureRuntimeState.ReconnectRequired;
        }
        Enqueue("runtime-issue", new
        {
            code = issue.Code,
            message = issue.UserMessage,
            occurredAt = issue.OccurredAt,
            requiresDiscordRestart
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
            _runtime?.IsPaused == true,
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
                    var offerAutomaticRestart =
                        DiscordAutomaticRestartPolicy.ShouldOffer(
                            settings.DiscordSupportEnabled,
                            processId,
                            argumentState);
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
                    if (offerAutomaticRestart)
                    {
                        Enqueue("discord-auto-restart-required", new
                        {
                            processId,
                            countdownSeconds =
                                DiscordAutomaticRestartPolicy.CountdownSeconds
                        });
                    }
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

    private async Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await ApplyAutomaticCleanupAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunLinkPreviewLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var updated = 0;
                try
                {
                    updated = await EnrichLinkPreviewsOnceAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"link-preview-failed\t{exception.Message.Replace('\r', ' ').Replace('\n', ' ')}");
                }

                if (updated == 4)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                await _linkPreviewWakeSignal.WaitAsync(
                    TimeSpan.FromMinutes(15),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal async Task<int> EnrichLinkPreviewsOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var updated = await _enrichLinkPreviews(
            4,
            DateTimeOffset.UtcNow.AddDays(-30),
            cancellationToken);
        if (updated > 0)
        {
            Enqueue("gallery-changed", new
            {
                reason = "link-preview",
                updated
            });
        }
        return updated;
    }

    private void WakeLinkPreviewWorker()
    {
        if (_linkPreviewWakeSignal.CurrentCount != 0)
        {
            return;
        }
        try
        {
            _linkPreviewWakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    internal async Task<OcrEnrichmentBatchResult> EnrichOcrOnceAsync(
        CancellationToken cancellationToken = default)
    {
        if (_enrichImageOcr is null)
        {
            return new OcrEnrichmentBatchResult(0, 0);
        }

        var result = await _enrichImageOcr(1, cancellationToken);
        if (result.Updated > 0)
        {
            Enqueue("gallery-changed", new
            {
                reason = "image-ocr",
                updated = result.Updated
            });
        }
        return result;
    }

    private async Task RunOcrLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                OcrEnrichmentBatchResult result;
                try
                {
                    result = await EnrichOcrOnceAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"image-ocr-failed {exception.GetType().Name}: " +
                        exception.Message);
                    result = new OcrEnrichmentBatchResult(0, 0);
                }

                if (result.Attempted == 1)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(2),
                        cancellationToken);
                    continue;
                }

                _ocrRecognizer?.ReleaseModels();
                await _ocrWakeSignal.WaitAsync(
                    TimeSpan.FromMinutes(15),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void WakeOcrWorker()
    {
        if (_ocrWakeSignal.CurrentCount != 0)
        {
            return;
        }
        try
        {
            _ocrWakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task RunSyncLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            while (true)
            {
                await RunConfiguredSyncOnceAsync(cancellationToken);
                await _syncWakeSignal.WaitAsync(
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal async Task RunConfiguredSyncOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = _settingsStore.Load();
        if (!settings.SyncEnabled ||
            !HasCompleteSyncConfiguration(settings) ||
            !SyncDeviceIdentity.IsValid(settings.SyncDeviceId))
        {
            PublishSyncStatus(SyncRuntimeState.Disabled);
            return;
        }

        PublishSyncStatus(
            settings.SyncProvider == SentorySettings.FolderSyncProvider &&
            (settings.SyncStorageVersion <
                 SentorySettings.CurrentSyncStorageVersion ||
             settings.SyncMigrationDeviceId is not null)
                ? SyncRuntimeState.Migrating
                : SyncRuntimeState.Syncing);
        try
        {
            var migrationProjected = 0;
            LocalFolderSyncRunResult result;
            _syncRuntimeService ??= new LocalFolderSyncRuntimeService(
                _paths,
                _repository,
                _settingsStore,
                () => PublishSyncStatus(SyncRuntimeState.Recovering));
            if (settings.SyncProvider == SentorySettings.WebDavSyncProvider)
            {
                var store = GetWebDavStore(settings);
                var remoteStoreId = await GetOrCreateWebDavStoreIdAsync(
                    store,
                    cancellationToken);
                if (!string.Equals(
                        settings.SyncStoreId,
                        remoteStoreId,
                        StringComparison.Ordinal))
                {
                    settings.SyncDeviceId = SyncDeviceIdentity.Create();
                    settings.SyncStoreId = remoteStoreId;
                    await EnsureSyncJournalInitializedAsync(
                        null,
                        settings.SyncDeviceId,
                        cancellationToken);
                    await SqliteSyncOperationJournal.ResetForNewStoreAsync(
                        _paths,
                        settings.SyncDeviceId,
                        cancellationToken);
                    _settingsStore.Save(settings);
                    PublishSyncStatus(SyncRuntimeState.Recovering);
                }

                result = await _syncRuntimeService.RunObjectStoreOnceAsync(
                    settings.SyncDeviceId!,
                    store,
                    cancellationToken: cancellationToken);
            }
            else
            {
                var migration = await new SyncStorageMigrationService(
                    _paths,
                    _repository,
                    _settingsStore).MigrateIfNeededAsync(
                    settings,
                    cancellationToken);
                if (migration.Migrated)
                {
                    settings = _settingsStore.Load();
                    migrationProjected = migration.LegacyProjected;
                    PublishSyncStatus(SyncRuntimeState.Syncing);
                }

                result = await _syncRuntimeService.RunOnceAsync(
                    settings.SyncDeviceId!,
                    settings.SyncFolderPath!,
                    cancellationToken);
            }

            var succeededAt = DateTimeOffset.UtcNow;
            _syncStatusTracker.Update(
                SyncRuntimeState.Succeeded,
                succeededAt,
                succeededAt);
            Enqueue("sync-status", CreateSyncSettingsDto(
                _settingsStore.Load()));
            if (result.Metadata.SettingsChanged)
            {
                Enqueue("settings-changed", CreateSettingsDto(
                    _settingsStore.Load()));
            }
            if (result.Cycle.Projection.Projected > 0 ||
                result.Metadata.Projected > 0 ||
                migrationProjected > 0)
            {
                Enqueue("gallery-changed", new
                {
                    reason = "sync",
                    projected = result.Cycle.Projection.Projected +
                                result.Metadata.Projected +
                                migrationProjected
                });
                WakeLinkPreviewWorker();
                WakeOcrWorker();
            }
            if (result.Export.Exported == 200 ||
                result.Publish.Downloaded > 0)
            {
                WakeSyncWorker();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SyncStoreUnavailableException exception)
        {
            PublishSyncStatus(SyncRuntimeState.FolderUnavailable);
            Enqueue("sync-issue", new { message = exception.Message });
        }
        catch (InvalidDataException exception)
        {
            PublishSyncStatus(SyncRuntimeState.InvalidData);
            Enqueue("sync-issue", new { message = exception.Message });
        }
        catch (Exception exception)
        {
            PublishSyncStatus(SyncRuntimeState.Failed);
            Enqueue("sync-issue", new { message = exception.Message });
        }
    }

    private void WakeSyncWorker()
    {
        if (_syncWakeSignal.CurrentCount != 0)
        {
            return;
        }
        try
        {
            _syncWakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private void PublishSyncStatus(SyncRuntimeState state)
    {
        _syncStatusTracker.Update(state, DateTimeOffset.UtcNow);
        Enqueue("sync-status", CreateSyncSettingsDto(
            _settingsStore.Load()));
    }

    private WebDavSyncObjectStore GetWebDavStore(
        SentorySettings settings)
    {
        var configuration = CreateWebDavConfigurationKey(settings);
        lock (_webDavStoreGate)
        {
            if (_webDavStore is not null &&
                string.Equals(
                    _webDavStoreConfiguration,
                    configuration,
                    StringComparison.Ordinal))
            {
                return _webDavStore;
            }

            var replacement = CreateWebDavStore(settings);
            _webDavStore?.Dispose();
            _webDavStore = replacement;
            _webDavStoreConfiguration = configuration;
            return replacement;
        }
    }

    private void ReplaceWebDavStore(
        WebDavSyncObjectStore? store,
        string? configuration)
    {
        lock (_webDavStoreGate)
        {
            _webDavStore?.Dispose();
            _webDavStore = store;
            _webDavStoreConfiguration = configuration;
        }
    }

    private static WebDavSyncObjectStore CreateWebDavStore(
        SentorySettings settings) =>
        new(
            settings.SyncWebDavEndpoint ??
            throw new InvalidOperationException(
                "NAS WebDAV 주소가 설정되지 않았습니다."),
            settings.SyncWebDavUsername,
            string.IsNullOrWhiteSpace(settings.SyncWebDavProtectedPassword)
                ? null
                : WebDavCredentialProtector.Unprotect(
                    settings.SyncWebDavProtectedPassword));

    private static string CreateWebDavConfigurationKey(
        SentorySettings settings) =>
        string.Join(
            '\n',
            settings.SyncWebDavEndpoint,
            settings.SyncWebDavUsername,
            settings.SyncWebDavProtectedPassword);

    private static string? ResolveWebDavPassword(
        SentorySettings settings,
        string endpoint,
        string? username,
        string? password)
    {
        if (password is not null)
        {
            return password;
        }
        if (settings.SyncProvider != SentorySettings.WebDavSyncProvider ||
            !WebDavEndpointsEqual(settings.SyncWebDavEndpoint, endpoint) ||
            !string.Equals(
                settings.SyncWebDavUsername,
                username,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(settings.SyncWebDavProtectedPassword))
        {
            return null;
        }

        return WebDavCredentialProtector.Unprotect(
            settings.SyncWebDavProtectedPassword);
    }

    private static bool WebDavEndpointsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(
            left.TrimEnd('/'),
            right.Trim().TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    private static bool HasCompleteSyncConfiguration(
        SentorySettings settings) =>
        settings.SyncProvider switch
        {
            SentorySettings.WebDavSyncProvider =>
                !string.IsNullOrWhiteSpace(settings.SyncWebDavEndpoint),
            _ => !string.IsNullOrWhiteSpace(settings.SyncFolderPath)
        };

    private static async Task<string> GetOrCreateWebDavStoreIdAsync(
        WebDavSyncObjectStore store,
        CancellationToken cancellationToken)
    {
        const string key = ".sentory/v2/store.json";
        var stored = await store.TryGetAsync(key, cancellationToken);
        if (stored is not null)
        {
            return ReadWebDavStoreId(stored.Content);
        }

        var manifest = new WebDavStoreManifest(
            1,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow);
        var content = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            BridgeServer.JsonOptions);
        var sha256 = Convert.ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();
        await store.PutIfAbsentAsync(
            key,
            content,
            sha256,
            cancellationToken);
        stored = await store.TryGetAsync(key, cancellationToken) ??
                 throw new SyncStoreUnavailableException(
                     "NAS 동기화 저장소 식별 파일을 읽지 못했습니다.");
        return ReadWebDavStoreId(stored.Content);
    }

    private static string ReadWebDavStoreId(byte[] content)
    {
        WebDavStoreManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<WebDavStoreManifest>(
                content,
                BridgeServer.JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "NAS 동기화 저장소 식별 파일을 읽을 수 없습니다.",
                exception);
        }
        if (manifest is null ||
            manifest.FormatVersion != 1 ||
            manifest.StoreId.Length != 32 ||
            !Guid.TryParseExact(manifest.StoreId, "N", out _))
        {
            throw new InvalidDataException(
                "NAS 동기화 저장소 식별 정보가 올바르지 않습니다.");
        }
        return manifest.StoreId.ToLowerInvariant();
    }

    private static SyncConfigurationSnapshot CaptureSyncConfiguration(
        SentorySettings settings) =>
        new(
            settings.SyncEnabled,
            settings.SyncProvider,
            settings.SyncFolderPath,
            settings.SyncWebDavEndpoint,
            settings.SyncWebDavUsername,
            settings.SyncWebDavProtectedPassword,
            settings.SyncDeviceId,
            settings.SyncStorageVersion,
            settings.SyncMigrationDeviceId,
            settings.SyncStoreId);

    private async Task EnsureSyncJournalInitializedAsync(
        string? existingDeviceId,
        string newDeviceId,
        CancellationToken cancellationToken)
    {
        var initializationDeviceId = SyncDeviceIdentity.IsValid(
            existingDeviceId)
            ? existingDeviceId!
            : newDeviceId;
        try
        {
            await new SqliteSyncOperationJournal(
                _paths,
                initializationDeviceId).InitializeAsync(
                cancellationToken);
        }
        catch (SyncDeviceBindingMismatchException)
        {
            // InitializeAsync creates the schema before reporting the stale
            // device binding. The reset below intentionally replaces it.
        }
    }

    private static void RestoreSyncConfiguration(
        SentorySettings settings,
        SyncConfigurationSnapshot snapshot)
    {
        settings.SyncEnabled = snapshot.Enabled;
        settings.SyncProvider = snapshot.Provider;
        settings.SyncFolderPath = snapshot.FolderPath;
        settings.SyncWebDavEndpoint = snapshot.WebDavEndpoint;
        settings.SyncWebDavUsername = snapshot.WebDavUsername;
        settings.SyncWebDavProtectedPassword = snapshot.ProtectedPassword;
        settings.SyncDeviceId = snapshot.DeviceId;
        settings.SyncStorageVersion = snapshot.StorageVersion;
        settings.SyncMigrationDeviceId = snapshot.MigrationDeviceId;
        settings.SyncStoreId = snapshot.StoreId;
    }

    private async Task ApplyAutomaticCleanupAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _automaticCleanup.RunIfDueAsync(
                DateTimeOffset.UtcNow,
                cancellationToken);
            if (result is { Deleted.TotalItems: > 0 })
            {
                Enqueue("automatic-cleanup", new
                {
                    deleted = result.Deleted.TotalItems,
                    result.FileDeleteFailures
                });
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            lock (_stateGate)
            {
                _lastIssueCode = "auto-cleanup-failed";
                _lastIssue = exception.Message;
            }
            Enqueue("runtime-issue", new
            {
                code = "auto-cleanup-failed",
                message = exception.Message
            });
        }
    }

    private EngineSettingsDto CreateSettingsDto(SentorySettings settings) => new(
        settings.GetThemeMode().ToString(),
        settings.Language,
        settings.StartWithWindows ?? false,
        settings.AutoFavoriteEnabled,
        settings.AutoFavoriteCopyThreshold,
        settings.AutoCleanupDays,
        settings.MessengerDetectionSetupCompleted,
        settings.DiscordAutoRestartConsentGranted,
        CreateSourceStates(settings),
        MessengerAvailabilityProbe.Detect(),
        CreateSyncSettingsDto(settings));

    private EngineSyncSettingsDto CreateSyncSettingsDto(
        SentorySettings settings)
    {
        var snapshot = _syncStatusTracker.Current;
        return new EngineSyncSettingsDto(
            settings.SyncEnabled,
            settings.SyncProvider,
            settings.SyncFolderPath,
            settings.SyncWebDavEndpoint,
            settings.SyncWebDavUsername,
            !string.IsNullOrWhiteSpace(
                settings.SyncWebDavProtectedPassword),
            snapshot.State.ToString(),
            snapshot.LastSucceededAt);
    }

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

    private sealed record WebDavStoreManifest(
        int FormatVersion,
        string StoreId,
        DateTimeOffset CreatedAt);

    private sealed record SyncConfigurationSnapshot(
        bool Enabled,
        string Provider,
        string? FolderPath,
        string? WebDavEndpoint,
        string? WebDavUsername,
        string? ProtectedPassword,
        string? DeviceId,
        int StorageVersion,
        string? MigrationDeviceId,
        string? StoreId);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        if (!_started)
        {
            DisposeOwnedResources();
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
        if (_maintenanceTask is not null)
        {
            try
            {
                await _maintenanceTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (_linkPreviewTask is not null)
        {
            try
            {
                await _linkPreviewTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (_ocrTask is not null)
        {
            try
            {
                await _ocrTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (_syncTask is not null)
        {
            try
            {
                await _syncTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        if (_updateDownloadTask is not null)
        {
            try
            {
                await _updateDownloadTask;
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
        DisposeOwnedResources();
    }

    private void DisposeOwnedResources()
    {
        ReplaceWebDavStore(null, null);
        _linkPreviewFetcher?.Dispose();
        _ocrRecognizer?.Dispose();
        _linkPreviewWakeSignal.Dispose();
        _ocrWakeSignal.Dispose();
        _syncWakeSignal.Dispose();
        _updateCheckGate.Dispose();
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
    bool MessengerDetectionSetupCompleted,
    bool DiscordAutoRestartConsentGranted,
    IReadOnlyDictionary<string, bool> Sources,
    IReadOnlyDictionary<string, bool> AvailableSources,
    EngineSyncSettingsDto Sync);

public sealed record EngineStartupPreferenceDto(
    bool SettingsFileExisted,
    bool? SavedPreference);

public sealed record EngineSyncSettingsDto(
    bool Enabled,
    string Provider,
    string? FolderPath,
    string? WebDavEndpoint,
    string? WebDavUsername,
    bool WebDavPasswordSet,
    string State,
    DateTimeOffset? LastSucceededAt);

public sealed record EngineSyncFolderCandidateDto(
    string ProviderId,
    string ProviderName,
    string FolderPath,
    string DisplayName);

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
    bool? MessengerDetectionSetupCompleted = null,
    bool? DiscordAutoRestartConsentGranted = null,
    bool? StartWithWindows = null,
    bool? AutoFavoriteEnabled = null,
    int? AutoFavoriteCopyThreshold = null,
    int? AutoCleanupDays = null);

public sealed record EngineRuntimeStatusDto(
    string DiscordState,
    bool DiscordRunning,
    bool DetectionPaused,
    string? LastIssueCode,
    string? LastIssue,
    IReadOnlyDictionary<string, bool> Sources);

public sealed record EngineRuntimeEventDto(string Type, object Payload);

public sealed record EngineRuntimePollDto(
    EngineRuntimeStatusDto Status,
    IReadOnlyList<EngineRuntimeEventDto> Events);

public sealed record EngineUpdateCheckDto(
    bool Checked,
    bool UpdateAvailable,
    bool ReadyToInstall,
    string? Version,
    string? ReleasePage,
    string? PackageKind);

public sealed record EngineUpdateInstallDto(
    bool Launched,
    string Version);

public sealed record EngineCaptureEventDto(
    string Kind,
    int Count,
    DateTimeOffset CapturedAt,
    string? SourceApp,
    string? DeliveryStatus);
