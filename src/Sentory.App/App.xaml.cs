using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Sentory.Core;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Links;
using Sentory.Infrastructure.Ocr;
using Sentory.Infrastructure.Sync;
using Sentory.Infrastructure.Updates;
using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Ocr;
using Sentory.Platform.Windows.Runtime;
using Forms = System.Windows.Forms;

namespace Sentory.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName =
        "Local\\Sentory.Desktop.Singleton";
    private const string OpenGalleryEventName =
        "Local\\Sentory.Desktop.OpenGallery";
    private const string InstallationVerificationArgument =
        "--verify-installation";

    private readonly SentoryDataPaths _paths =
        SentoryDataPaths.FromEnvironmentOrCurrentUser(
            Environment.GetEnvironmentVariable("SENTORY_DATA_DIR"));
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _openGalleryEvent;
    private RegisteredWaitHandle? _openGalleryRegistration;
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _trayIconImage;
    private TrayMenuWindow? _trayMenuWindow;
    private string _statusText = "시작 중...";
    private ICaptureRepository? _repository;
    private SentorySettingsStore? _settingsStore;
    private SentoryDiagnosticsLog? _diagnosticsLog;
    private ICaptureRuntime? _runtime;
    private KakaoDropOverlayRuntime? _kakaoDropOverlay;
    private DiscordDropOverlayRuntime? _discordDropOverlay;
    private SlackDropOverlayRuntime? _slackDropOverlay;
    private WhatsAppDropOverlayRuntime? _whatsAppDropOverlay;
    private TelegramDropOverlayRuntime? _telegramDropOverlay;
    private LineDropOverlayRuntime? _lineDropOverlay;
    private GalleryWindow? _galleryWindow;
    private readonly CancellationTokenSource _maintenanceCancellation = new();
    private Task? _maintenanceTask;
    private readonly SemaphoreSlim _linkPreviewWakeSignal = new(0, 1);
    private LinkPreviewFetcher? _linkPreviewFetcher;
    private LinkPreviewEnrichmentService? _linkPreviewService;
    private Task? _linkPreviewTask;
    private readonly SemaphoreSlim _ocrWakeSignal = new(0, 1);
    private OcrEnrichmentService? _ocrService;
    private PaddleOcrImageTextRecognizer? _ocrRecognizer;
    private Task? _ocrTask;
    private readonly SemaphoreSlim _syncWakeSignal = new(0, 1);
    private readonly SyncRuntimeStatusTracker _syncStatusTracker = new();
    private Task? _syncTask;
    private readonly WindowsStartupManager _startupManager = new();
    private readonly DiscordAccessibilityLauncher _discordLauncher = new();
    private readonly DiscordStartupRegistrationManager
        _discordStartupRegistration = new();
    private bool _discordSupportEnabled = true;
    private bool _kakaoSupportEnabled = true;
    private bool _slackSupportEnabled = true;
    private bool _whatsAppSupportEnabled = true;
    private bool _telegramSupportEnabled = true;
    private bool _lineSupportEnabled = true;
    private bool _weChatSupportEnabled = true;
    private bool _discordRepairNeeded;
    private bool _discordRepairBusy;
    private bool _discordRestartPromptActive;
    private int? _observedDiscordProcessId;
    private int? _automaticRestartPromptedProcessId;
    private DiscordAccessibilityArgumentState
        _observedDiscordAccessibilityArgumentState =
            DiscordAccessibilityArgumentState.Unknown;
    private CaptureRuntimeState _discordDetectionState =
        CaptureRuntimeState.Connecting;
    private Task? _discordConnectionMonitorTask;
    private bool _shuttingDown;
    private string? _lastRuntimeIssueCode;
    private string? _lastRuntimeIssue;
    private readonly GitHubReleaseUpdateClient _updateClient = new();
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);
    private ReleaseUpdate? _availableUpdate;
    private string? _downloadedUpdatePackage;
    private bool _updateInstallationInProgress;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (InstallerUpdateApplier.IsLaunchCommand(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await InstallerUpdateApplier.RunAsync(e.Args));
            return;
        }

        if (PortableUpdateApplier.IsApplyCommand(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await PortableUpdateApplier.RunAsync(e.Args));
            return;
        }

        if (e.Args.Contains(
                DiscordStartupRegistrationManager.RestoreArgument,
                StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                _discordStartupRegistration.Restore();
                Shutdown(0);
            }
            catch (Exception exception)
                when (exception is IOException or
                      UnauthorizedAccessException or
                      System.Security.SecurityException)
            {
                Shutdown(1);
            }

            return;
        }

        var settingsFileExisted = File.Exists(_paths.SettingsPath);
        _settingsStore = new SentorySettingsStore(_paths);
        var initialSettings = _settingsStore.Load();
        SentoryLocalization.Apply(
            Resources,
            initialSettings.Language);
        _statusText = SentoryLocalization.Text("Starting");
        _diagnosticsLog = new SentoryDiagnosticsLog(_paths);
        var isInstallationVerification = e.Args.Contains(
            InstallationVerificationArgument,
            StringComparer.OrdinalIgnoreCase);

        if (e.Args.Contains(
                DiscordWorkerClient.WorkerArgument,
                StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            using var workerInput = new StreamReader(
                Console.OpenStandardInput());
            using var workerOutput = new StreamWriter(
                Console.OpenStandardOutput())
            {
                AutoFlush = true
            };
            var exitCode = await DiscordAccessibilityWorker.RunAsync(
                workerInput,
                workerOutput);
            Shutdown(exitCode);
            return;
        }

        if (isInstallationVerification)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        else
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                SingleInstanceMutexName,
                out var createdNew);
            _ownsMutex = createdNew;
            if (!createdNew)
            {
                if (!RequestGalleryFromRunningInstance())
                {
                    SentoryDialogWindow.ShowMessage(
                        null,
                        SentoryLocalization.Text("AlreadyRunningHeading"),
                        SentoryLocalization.Text("AlreadyRunningMessage"),
                        GetSavedDarkTheme());
                }

                Shutdown();
                return;
            }

            RegisterGalleryOpenSignal();
        }

        DisplayNamedImageFile.CleanupOldCopies(TimeSpan.FromDays(7));

        try
        {
            if (!isInstallationVerification)
            {
                ApplyInitialStartupPreference(
                    settingsFileExisted,
                    initialSettings);
                SynchronizeDiscordStartupRegistration(
                    initialSettings.DiscordSupportEnabled,
                    GetStartupEnabled());
            }

            PrepareDiscordDefault();
            var captureRepository = new SqliteCaptureRepository(_paths);
            captureRepository.ConfigureAutomaticFavorites(
                initialSettings.AutoFavoriteEnabled,
                initialSettings.AutoFavoriteCopyThreshold);
            _repository = captureRepository;
            await _repository.InitializeAsync();
            var repairResult = await _repository.RepairStorageAsync();
            _linkPreviewFetcher = new LinkPreviewFetcher(_paths);
            _linkPreviewService = new LinkPreviewEnrichmentService(
                _repository,
                _linkPreviewFetcher);

            if (isInstallationVerification)
            {
                _diagnosticsLog.Write(
                    "installation-verified",
                    "Portable package storage initialization succeeded");
                _linkPreviewFetcher.Dispose();
                _linkPreviewFetcher = null;
                _linkPreviewService = null;
                Shutdown(0);
                return;
            }

            var ocrDisabled = OcrRuntimePolicy.IsDisabled(
                Environment.GetEnvironmentVariable(
                    OcrRuntimePolicy.DisableEnvironmentVariable));
            if (ocrDisabled)
            {
                _diagnosticsLog.Write(
                    "image-ocr-disabled",
                    $"OCR was disabled by {OcrRuntimePolicy.DisableEnvironmentVariable}");
            }
            else
            {
                var ocrRecognizer = new PaddleOcrImageTextRecognizer(
                    Path.Combine(_paths.RootDirectory, "ocr-models"),
                    new WindowsImageTextRecognizer());
                _ocrRecognizer = ocrRecognizer;
                _ocrService = new OcrEnrichmentService(
                    (IImageOcrRepository)_repository,
                    ocrRecognizer,
                    _paths,
                    (sha256, exception) => _diagnosticsLog?.Write(
                        "image-ocr-item-failed",
                        $"Image OCR failed for {sha256[..Math.Min(12, sha256.Length)]}",
                        exception),
                    new WindowsImageMetadataTitleReader());
                if (!ocrRecognizer.IsAvailable)
                {
                    _diagnosticsLog.Write(
                        "image-ocr-unavailable",
                        "No local OCR engine is available");
                }
            }

            var acceptInjectedInput = string.Equals(
                Environment.GetEnvironmentVariable(
                    "SENTORY_ACCEPT_INJECTED_INPUT"),
                "1",
                StringComparison.Ordinal);
            var kakaoRuntime = new KakaoCaptureRuntime(
                _repository,
                acceptInjectedInput);
            var discordRuntime = new DiscordCaptureRuntime(
                _repository,
                acceptInjectedInput);
            var slackRuntime = new SlackCaptureRuntime(
                _repository,
                acceptInjectedInput,
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            var whatsAppRuntime = new WhatsAppCaptureRuntime(
                _repository,
                acceptInjectedInput,
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            var telegramRuntime = new TelegramCaptureRuntime(
                _repository,
                acceptInjectedInput,
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            var lineRuntime = new LineCaptureRuntime(
                _repository,
                acceptInjectedInput,
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            var weChatRuntime = new WeChatCaptureRuntime(
                _repository,
                acceptInjectedInput,
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            _runtime = new CompositeCaptureRuntime(
                (SourceApp.KakaoTalk, kakaoRuntime),
                (SourceApp.Discord, discordRuntime),
                (SourceApp.Slack, slackRuntime),
                (SourceApp.WhatsApp, whatsAppRuntime),
                (SourceApp.Telegram, telegramRuntime),
                (SourceApp.Line, lineRuntime),
                (SourceApp.WeChat, weChatRuntime));
            _kakaoDropOverlay = new KakaoDropOverlayRuntime(
                kakaoRuntime,
                GetSavedDarkTheme,
                () => SentoryLocalization.Text("KakaoDropHeading"),
                () => SentoryLocalization.Text("KakaoDropDescription"),
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            _discordDropOverlay = new DiscordDropOverlayRuntime(
                discordRuntime,
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            _slackDropOverlay = new SlackDropOverlayRuntime(
                slackRuntime,
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            _whatsAppDropOverlay = new WhatsAppDropOverlayRuntime(
                whatsAppRuntime,
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            _telegramDropOverlay = new TelegramDropOverlayRuntime(
                telegramRuntime,
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            _lineDropOverlay = new LineDropOverlayRuntime(
                lineRuntime,
                (category, message) =>
                    _diagnosticsLog?.Write(category, message));
            _runtime.Captured += OnCaptured;
            _runtime.IssueDetected += OnCaptureIssueDetected;
            if (_runtime is ICaptureRuntimeStatusSource statusSource)
            {
                statusSource.StatusChanged += OnCaptureStatusChanged;
            }

            CreateTrayIcon();
            if (repairResult.FileDeleteFailures > 0 ||
                repairResult.MissingImageFiles > 0)
            {
                _lastRuntimeIssueCode = "storage-repair";
                _lastRuntimeIssue =
                    SentoryLocalization.Text("StorageRepairIssue");
                _diagnosticsLog.Write(
                    "storage-repair",
                    $"missing={repairResult.MissingImageFiles}, deleteFailures={repairResult.FileDeleteFailures}");
                _trayIcon?.ShowBalloonTip(
                    3000,
                    SentoryLocalization.Text("StorageCheckTitle"),
                    SentoryLocalization.Text("StorageCheckMessage"),
                    Forms.ToolTipIcon.Warning);
            }
            await ApplyAutomaticCleanupAsync();
            _maintenanceTask = RunMaintenanceLoopAsync(
                _maintenanceCancellation.Token);
            _linkPreviewTask = RunLinkPreviewLoopAsync(
                _maintenanceCancellation.Token);
            if (_ocrService is not null)
            {
                _ocrTask = Task.Run(() => RunOcrLoopAsync(
                    _maintenanceCancellation.Token));
            }
            _syncTask = Task.Run(() => RunSyncLoopAsync(
                _maintenanceCancellation.Token));
            ApplyRuntimeSourceSettings();
            _runtime.Start();
            _discordConnectionMonitorTask =
                RunDiscordConnectionMonitorAsync(
                    _maintenanceCancellation.Token);
            _kakaoDropOverlay.Start();
            _discordDropOverlay.Start();
            _slackDropOverlay.Start();
            _whatsAppDropOverlay.Start();
            _telegramDropOverlay.Start();
            _lineDropOverlay.Start();
            UpdatePauseUi();
            OpenGallery();
            _ = CheckForUpdatesAsync(_maintenanceCancellation.Token);
        }
        catch (Exception exception)
        {
            _diagnosticsLog?.Write(
                "startup-failed",
                "Sentory startup failed",
                exception);
            if (!isInstallationVerification)
            {
                SentoryDialogWindow.ShowMessage(
                null,
                SentoryLocalization.Text("StartupFailedHeading"),
                    exception.Message,
                    GetSavedDarkTheme(),
                    danger: true);
            }
            await ShutdownRuntimeAsync();
            Shutdown(1);
        }
    }

    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
            await CheckForUpdatesCoreAsync(
                ignoreCooldown: false,
                _galleryWindow,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _diagnosticsLog?.Write("update-check-failed", "Update check failed", exception);
            if (_settingsStore is not null)
            {
                var settings = _settingsStore.Load();
                settings.LastUpdateCheckAt = null;
                _settingsStore.Save(settings);
            }
        }
    }

    private async Task<ManualUpdateCheckResult> CheckForUpdatesManuallyAsync(
        Window owner)
    {
        try
        {
            return await CheckForUpdatesCoreAsync(
                ignoreCooldown: true,
                owner,
                _maintenanceCancellation.Token);
        }
        catch (OperationCanceledException)
            when (_maintenanceCancellation.IsCancellationRequested)
        {
            return new ManualUpdateCheckResult(
                ManualUpdateCheckOutcome.Failed);
        }
        catch (Exception exception)
        {
            _diagnosticsLog?.Write(
                "manual-update-check-failed",
                "Manual update check failed",
                exception);
            ClearLastUpdateCheckAt();
            return new ManualUpdateCheckResult(
                ManualUpdateCheckOutcome.Failed);
        }
    }

    private async Task<ManualUpdateCheckResult> CheckForUpdatesCoreAsync(
        bool ignoreCooldown,
        Window? promptOwner,
        CancellationToken cancellationToken)
    {
        await _updateCheckGate.WaitAsync(cancellationToken);
        try
        {
            if (_settingsStore is null)
            {
                return new ManualUpdateCheckResult(
                    ManualUpdateCheckOutcome.Failed);
            }

            if (_availableUpdate is { } availableUpdate &&
                _downloadedUpdatePackage is { } availablePackage &&
                File.Exists(availablePackage))
            {
                if (promptOwner is not null)
                {
                    await PromptAndInstallUpdateOnUiAsync(
                        availableUpdate,
                        availablePackage,
                        promptOwner,
                        cancellationToken);
                }

                return new ManualUpdateCheckResult(
                    ManualUpdateCheckOutcome.UpdateAvailable,
                    availableUpdate.Version);
            }

            var settings = _settingsStore.Load();
            var now = DateTimeOffset.UtcNow;
            if (!UpdateCheckSchedule.ShouldCheck(
                    settings.LastUpdateCheckAt,
                    now,
                    ignoreCooldown))
            {
                return new ManualUpdateCheckResult(
                    ManualUpdateCheckOutcome.UpToDate);
            }

            settings.LastUpdateCheckAt = now;
            _settingsStore.Save(settings);
            var packageKind = File.Exists(Path.Combine(
                AppContext.BaseDirectory, "unins000.exe"))
                ? UpdatePackageKind.Installer
                : UpdatePackageKind.Portable;
            var currentVersion = SentoryBuildIdentity.CurrentVersion;
            _diagnosticsLog?.Write(
                ignoreCooldown
                    ? "manual-update-check-started"
                    : "update-check-started",
                $"Checking for an update from Sentory {currentVersion}");
            var update = await _updateClient.CheckAsync(
                currentVersion,
                RuntimeInformation.ProcessArchitecture,
                packageKind,
                cancellationToken);
            if (update is null || cancellationToken.IsCancellationRequested)
            {
                _diagnosticsLog?.Write(
                    ignoreCooldown
                        ? "manual-update-check-completed"
                        : "update-check-completed",
                    $"Sentory {currentVersion} is up to date");
                return new ManualUpdateCheckResult(
                    ManualUpdateCheckOutcome.UpToDate);
            }

            var directory = Path.Combine(
                Path.GetTempPath(), "Sentory", "downloads", update.Version);
            _diagnosticsLog?.Write(
                "update-download-started",
                $"Downloading Sentory {update.Version} update");
            var package = await _updateClient.DownloadAsync(
                update,
                directory,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _availableUpdate = update;
            _downloadedUpdatePackage = package;
            settings.LastUpdateCheckAt = null;
            _settingsStore.Save(settings);
            _diagnosticsLog?.Write(
                "update-download-completed",
                $"Sentory {update.Version} update is ready");

            await Dispatcher.InvokeAsync(
                () => _galleryWindow?.SetAvailableUpdate(update.Version));
            if (promptOwner is not null)
            {
                await PromptAndInstallUpdateOnUiAsync(
                    update,
                    package,
                    promptOwner,
                    cancellationToken);
            }

            return new ManualUpdateCheckResult(
                ManualUpdateCheckOutcome.UpdateAvailable,
                update.Version);
        }
        finally
        {
            _updateCheckGate.Release();
        }
    }

    private async Task PromptAndInstallUpdateOnUiAsync(
        ReleaseUpdate update,
        string package,
        Window owner,
        CancellationToken cancellationToken)
    {
        await await Dispatcher.InvokeAsync(async () =>
        {
            var activeOwner = owner.IsVisible ? owner : _galleryWindow;
            await PromptAndInstallUpdateAsync(
                update,
                package,
                cancellationToken,
                activeOwner);
        });
    }

    private void ClearLastUpdateCheckAt()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var settings = _settingsStore.Load();
        settings.LastUpdateCheckAt = null;
        _settingsStore.Save(settings);
    }

    private async void UpdateInstallRequested(object? sender, EventArgs e)
    {
        if (_availableUpdate is null ||
            _downloadedUpdatePackage is null ||
            _maintenanceCancellation is null)
        {
            return;
        }

        await PromptAndInstallUpdateAsync(
            _availableUpdate,
            _downloadedUpdatePackage,
            _maintenanceCancellation.Token,
            _galleryWindow);
    }

    private async Task PromptAndInstallUpdateAsync(
        ReleaseUpdate update,
        string package,
        CancellationToken cancellationToken,
        Window? owner)
    {
        if (_updateInstallationInProgress || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var presentation = UpdateAvailabilityUiPolicy.Resolve(
            update.Version,
            SentoryBuildIdentity.CurrentVersion,
            installationInProgress: false);
        if (!presentation.ShowInstallAction)
        {
            _availableUpdate = null;
            _downloadedUpdatePackage = null;
            _galleryWindow?.SetAvailableUpdate(null);
            _diagnosticsLog?.Write(
                "update-stale-candidate-ignored",
                $"Ignored Sentory {update.Version} because the current version is " +
                SentoryBuildIdentity.CurrentVersion);
            return;
        }

        var accepted = SentoryDialogWindow.Confirm(
            owner ?? _galleryWindow,
            SentoryLocalization.Text("UpdateAvailableHeading"),
            SentoryLocalization.Format(
                "UpdateAvailableMessage",
                update.Version),
            SentoryLocalization.Text("InstallUpdate"),
            GetSavedDarkTheme());
        if (!accepted)
        {
            return;
        }

        _updateInstallationInProgress = true;
        _galleryWindow?.SetAvailableUpdate(
            update.Version,
            installationInProgress: true);
        try
        {
            await ApplyDownloadedUpdateAsync(
                update,
                package,
                cancellationToken);
        }
        finally
        {
            _updateInstallationInProgress = false;
            if (!_shuttingDown)
            {
                _galleryWindow?.SetAvailableUpdate(update.Version);
            }
        }
    }

    private async Task ApplyDownloadedUpdateAsync(
        ReleaseUpdate update,
        string package,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(package))
            {
                throw new FileNotFoundException(
                    "The downloaded update package is missing.",
                    package);
            }

            if (update.PackageKind == UpdatePackageKind.Installer)
            {
                InstallerUpdateApplier.PrepareAndLaunch(
                    package,
                    _diagnosticsLog?.CurrentLogPath ?? Path.Combine(
                        _paths.LogsDirectory,
                        "sentory.log"));
            }
            else
            {
                PortableUpdateApplier.PrepareAndLaunch(package);
            }

            await ShutdownRuntimeAsync();
            Shutdown();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _diagnosticsLog?.Write("update-apply-failed", "Update download or apply failed", exception);
            SentoryDialogWindow.ShowMessage(
                _galleryWindow,
                SentoryLocalization.Text("UpdateFailedHeading"),
                SentoryLocalization.Text("UpdateFailedMessage"),
                GetSavedDarkTheme(),
                danger: true);
        }
    }

    private void CreateTrayIcon()
    {
        _trayIconImage = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Sentory",
            Icon = _trayIconImage ?? SystemIcons.Application,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => OpenGallery();
        _trayIcon.MouseUp += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Right)
            {
                ShowTrayMenu();
            }
        };
    }

    private void RegisterGalleryOpenSignal()
    {
        _openGalleryEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            OpenGalleryEventName);
        _openGalleryRegistration = ThreadPool.RegisterWaitForSingleObject(
            _openGalleryEvent,
            (_, timedOut) =>
            {
                if (timedOut || _shuttingDown)
                {
                    return;
                }

                Dispatcher.BeginInvoke(() =>
                {
                    if (!_shuttingDown)
                    {
                        OpenGallery();
                    }
                });
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private static bool RequestGalleryFromRunningInstance()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(
                    OpenGalleryEventName);
                return signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    private void ShowTrayMenu()
    {
        var menu = EnsureTrayMenu();
        menu.UpdateState(
            _statusText,
            _runtime?.IsPaused == true,
            GetStartupEnabled(),
            _discordSupportEnabled,
            _observedDiscordProcessId.HasValue,
            _discordDetectionState,
            _discordRepairNeeded,
            GetSavedDarkTheme());

        var cursor = Forms.Cursor.Position;
        menu.WindowStartupLocation = WindowStartupLocation.Manual;
        menu.Left = cursor.X;
        menu.Top = cursor.Y;
        if (!menu.IsVisible)
        {
            menu.Show();
        }

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(menu);
        var screen = Forms.Screen.FromPoint(cursor).WorkingArea;
        var widthInPixels = menu.ActualWidth * dpi.DpiScaleX;
        var heightInPixels = menu.ActualHeight * dpi.DpiScaleY;
        var leftInPixels = Math.Clamp(
            cursor.X - widthInPixels + 12,
            screen.Left,
            screen.Right - widthInPixels);
        var topInPixels = Math.Clamp(
            cursor.Y - heightInPixels + 12,
            screen.Top,
            screen.Bottom - heightInPixels);
        menu.Left = leftInPixels / dpi.DpiScaleX;
        menu.Top = topInPixels / dpi.DpiScaleY;
        menu.Activate();
    }

    private TrayMenuWindow EnsureTrayMenu()
    {
        _trayMenuWindow = TrayMenuReusePolicy.GetOrCreate(
            _trayMenuWindow,
            () =>
            {
                var menu = new TrayMenuWindow();
                menu.OpenGalleryRequested += (_, _) => OpenGallery();
                menu.PauseToggleRequested += (_, _) => ApplyPauseState();
                menu.StartupToggleRequested += (_, _) => ApplyStartupState();
                menu.DiscordSupportToggleRequested += (_, _) =>
                    ApplyDiscordSupportState();
                menu.DiscordRepairRequested += async (_, _) =>
                    await RepairDiscordConnectionAsync();
                menu.OpenDataRequested += (_, _) => OpenDataFolder();
                menu.ExitRequested += async (_, _) =>
                {
                    await ShutdownRuntimeAsync();
                    Shutdown();
                };
                return menu;
            });
        return _trayMenuWindow;
    }

    private void PrepareDiscordDefault()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var settings = _settingsStore.Load();
        _discordSupportEnabled = settings.DiscordSupportEnabled;
        _kakaoSupportEnabled = settings.KakaoTalkSupportEnabled;
        _slackSupportEnabled = settings.SlackSupportEnabled;
        _whatsAppSupportEnabled = settings.WhatsAppSupportEnabled;
        _telegramSupportEnabled = settings.TelegramSupportEnabled;
        _lineSupportEnabled = settings.LineSupportEnabled;
        _weChatSupportEnabled = settings.WeChatSupportEnabled;
        if (!_discordSupportEnabled || !_discordLauncher.IsInstalled)
        {
            return;
        }

        try
        {
            var preparation = DiscordStartupPreparationPolicy.Resolve(
                _discordSupportEnabled,
                _discordLauncher.IsInstalled,
                _discordLauncher.IsRunning(),
                settings.DiscordAccessibilityPrepared);
            if (preparation ==
                DiscordStartupPreparationAction.StartDiscord)
            {
                _discordLauncher.Start();
                settings.DiscordAccessibilityPrepared = true;
                _settingsStore.Save(settings);
            }

            _discordRepairNeeded = preparation ==
                DiscordStartupPreparationAction.RequireRestart;
        }
        catch (Exception exception)
            when (exception is IOException or
                  InvalidOperationException or
                  System.ComponentModel.Win32Exception or
                  UnauthorizedAccessException)
        {
            _discordRepairNeeded = true;
        }
    }

    private void ApplyDiscordSupportState()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var settings = _settingsStore.Load();
        settings.DiscordSupportEnabled =
            !settings.DiscordSupportEnabled;
        _settingsStore.Save(settings);
        ApplyDiscordSupportSetting();
    }

    private void ApplyDiscordSupportSetting()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var settings = _settingsStore.Load();
        _discordSupportEnabled = settings.DiscordSupportEnabled;
        if (!_discordSupportEnabled)
        {
            _discordRepairNeeded = false;
            _galleryWindow?.SetDiscordRepairNeeded(false);
            _settingsStore.Save(settings);
            ApplyRuntimeSourceSettings();
            _galleryWindow?.SetMessengerSupportState(
                _discordSupportEnabled,
                _kakaoSupportEnabled,
                _slackSupportEnabled,
                _whatsAppSupportEnabled,
                _telegramSupportEnabled,
                _lineSupportEnabled,
                _weChatSupportEnabled);
            QueueDiscordStartupRegistrationSync(
                discordSupportEnabled: false,
                GetStartupEnabled());
            UpdatePauseUi();
            return;
        }

        try
        {
            var preparation = DiscordStartupPreparationPolicy.Resolve(
                _discordSupportEnabled,
                _discordLauncher.IsInstalled,
                _discordLauncher.IsRunning(),
                settings.DiscordAccessibilityPrepared);
            if (preparation ==
                DiscordStartupPreparationAction.StartDiscord)
            {
                _discordLauncher.Start();
                settings.DiscordAccessibilityPrepared = true;
                _discordRepairNeeded = false;
            }
            else
            {
                _discordRepairNeeded = preparation ==
                    DiscordStartupPreparationAction.RequireRestart;
            }
        }
        catch (Exception exception)
            when (exception is IOException or
                  InvalidOperationException or
                  System.ComponentModel.Win32Exception or
                  UnauthorizedAccessException)
        {
            _discordRepairNeeded = true;
        }

        _settingsStore.Save(settings);
        ApplyRuntimeSourceSettings();
        _galleryWindow?.SetDiscordRepairNeeded(_discordRepairNeeded);
        _galleryWindow?.SetMessengerSupportState(
            _discordSupportEnabled,
            _kakaoSupportEnabled,
            _slackSupportEnabled,
            _whatsAppSupportEnabled,
            _telegramSupportEnabled,
            _lineSupportEnabled,
            _weChatSupportEnabled);
        QueueDiscordStartupRegistrationSync(
            _discordSupportEnabled,
            GetStartupEnabled());
        UpdatePauseUi();
    }

    private void ApplyMessengerSupportSetting(
        SourceApp sourceApp,
        bool enabled)
    {
        if (_settingsStore is null)
        {
            return;
        }

        if (sourceApp == SourceApp.Discord)
        {
            _discordSupportEnabled = enabled;
            ApplyDiscordSupportSetting();
            return;
        }

        if (sourceApp == SourceApp.KakaoTalk)
        {
            _kakaoSupportEnabled = enabled;
        }
        else if (sourceApp == SourceApp.Slack)
        {
            _slackSupportEnabled = enabled;
        }
        else if (sourceApp == SourceApp.WhatsApp)
        {
            _whatsAppSupportEnabled = enabled;
        }
        else if (sourceApp == SourceApp.Telegram)
        {
            _telegramSupportEnabled = enabled;
        }
        else if (sourceApp == SourceApp.Line)
        {
            _lineSupportEnabled = enabled;
        }
        else if (sourceApp == SourceApp.WeChat)
        {
            _weChatSupportEnabled = enabled;
        }
        else
        {
            return;
        }
        ApplyRuntimeSourceSettings();
        _galleryWindow?.SetMessengerSupportState(
            _discordSupportEnabled,
            _kakaoSupportEnabled,
            _slackSupportEnabled,
            _whatsAppSupportEnabled,
            _telegramSupportEnabled,
            _lineSupportEnabled,
            _weChatSupportEnabled);
        UpdatePauseUi();
    }

    private void ApplyRuntimeSourceSettings()
    {
        if (_runtime is not ICaptureRuntimeSourceController controller)
        {
            return;
        }

        controller.SetSourceEnabled(
            SourceApp.Discord,
            _discordSupportEnabled);
        controller.SetSourceEnabled(
            SourceApp.KakaoTalk,
            _kakaoSupportEnabled);
        controller.SetSourceEnabled(
            SourceApp.Slack,
            _slackSupportEnabled);
        controller.SetSourceEnabled(
            SourceApp.WhatsApp,
            _whatsAppSupportEnabled);
        controller.SetSourceEnabled(
            SourceApp.Telegram,
            _telegramSupportEnabled);
        controller.SetSourceEnabled(
            SourceApp.Line,
            _lineSupportEnabled);
        controller.SetSourceEnabled(
            SourceApp.WeChat,
            _weChatSupportEnabled);
    }

    private void ApplyPauseState()
    {
        if (_runtime is null)
        {
            return;
        }

        _runtime.IsPaused = !_runtime.IsPaused;
        _galleryWindow?.SetDetectionPaused(_runtime.IsPaused);
        UpdatePauseUi();
    }

    private void UpdatePauseUi()
    {
        if (_runtime is null)
        {
            return;
        }

        if (_runtime.IsPaused)
        {
            SetStatus(
                SentoryLocalization.Text("StatusPaused"),
                SentoryLocalization.Text("TrayPaused"));
            return;
        }

        var additionalMessengerEnabled =
            _slackSupportEnabled ||
            _whatsAppSupportEnabled ||
            _telegramSupportEnabled ||
            _lineSupportEnabled ||
            _weChatSupportEnabled;
        var anyMessengerEnabled =
            _discordSupportEnabled ||
            _kakaoSupportEnabled ||
            additionalMessengerEnabled;
        var statusKey = additionalMessengerEnabled
            ? "StatusDetectingMessengers"
            : (_discordSupportEnabled, _kakaoSupportEnabled) switch
        {
            (true, true) => "StatusDetecting",
            (true, false) => "StatusDetectingDiscord",
            (false, true) => "StatusDetectingKakao",
            _ => "StatusDetectionDisabled"
        };
        SetStatus(
            SentoryLocalization.Text(statusKey),
            SentoryLocalization.Text(
                anyMessengerEnabled
                    ? "TrayDetecting"
                    : "TrayDetectionDisabled"));
    }

    private bool GetStartupEnabled()
    {
        try
        {
            return _startupManager.IsEnabled();
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or
                  System.Security.SecurityException or
                  IOException)
        {
            return false;
        }
    }

    private void SynchronizeDiscordStartupRegistration(
        bool discordSupportEnabled,
        bool startupEnabled)
    {
        try
        {
            _discordStartupRegistration.Synchronize(
                discordSupportEnabled && startupEnabled);
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  System.Security.SecurityException)
        {
            _diagnosticsLog?.Write(
                "discord-startup-registration-failed",
                "Discord startup registration could not be synchronized",
                exception);
        }
    }

    private void QueueDiscordStartupRegistrationSync(
        bool discordSupportEnabled,
        bool startupEnabled) =>
        _ = Task.Run(() => SynchronizeDiscordStartupRegistration(
            discordSupportEnabled,
            startupEnabled));

    private Task SynchronizeDiscordStartupRegistrationAsync(
        bool discordSupportEnabled,
        bool startupEnabled) =>
        Task.Run(() => SynchronizeDiscordStartupRegistration(
            discordSupportEnabled,
            startupEnabled));

    private void ApplyInitialStartupPreference(
        bool settingsFileExisted,
        SentorySettings settings)
    {
        try
        {
            var enabled = StartupPreferencePolicy.Resolve(
                settingsFileExisted,
                settings.StartWithWindows,
                _startupManager.IsEnabled());
            _startupManager.SetEnabled(enabled);
            if (settings.StartWithWindows != enabled)
            {
                settings.StartWithWindows = enabled;
                _settingsStore!.Save(settings);
            }
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or
                  System.Security.SecurityException or
                  IOException or
                  InvalidOperationException)
        {
            _diagnosticsLog?.Write(
                "startup-registration-failed",
                "Windows startup preference could not be applied",
                exception);
        }
    }

    private bool GetSavedDarkTheme()
    {
        try
        {
            var settings =
                (_settingsStore ?? new SentorySettingsStore(_paths)).Load();
            return SentoryThemePreference.ResolveIsDark(
                settings.GetThemeMode(),
                SentoryThemePreference.ReadWindowsIsDark());
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ApplyStartupState()
    {
        var enabled = !GetStartupEnabled();
        try
        {
            _startupManager.SetEnabled(enabled);
            if (_settingsStore is not null)
            {
                var settings = _settingsStore.Load();
                settings.StartWithWindows = enabled;
                _settingsStore.Save(settings);
                QueueDiscordStartupRegistrationSync(
                    settings.DiscordSupportEnabled,
                    enabled);
            }
            _trayIcon?.ShowBalloonTip(
                1800,
                "Sentory",
                enabled
                    ? SentoryLocalization.Text("StartupEnabled")
                    : SentoryLocalization.Text("StartupDisabled"),
                Forms.ToolTipIcon.Info);
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or
                  System.Security.SecurityException or
                  IOException or
                  InvalidOperationException)
        {
            _trayIcon?.ShowBalloonTip(
                2200,
                "Sentory",
                SentoryLocalization.Text("StartupChangeFailed"),
                Forms.ToolTipIcon.Warning);
        }
    }

    private void OnCaptured(
        object? sender,
        CaptureNotification notification)
    {
        WakeSyncWorker();
        Dispatcher.BeginInvoke(() =>
        {
            _lastRuntimeIssueCode = null;
            _lastRuntimeIssue = null;
            _galleryWindow?.SetRuntimeIssue(null);
            _trayIcon?.ShowBalloonTip(
                2500,
                "Sentory",
                notification.SourceApp == SourceApp.Discord &&
                notification.DeliveryStatus == DeliveryStatus.Confirmed
                    ? notification.Kind == ContentKind.Collection
                        ? SentoryLocalization.Text("DiscordCollectionSaved")
                    : notification.Kind == ContentKind.Image
                        ? SentoryLocalization.Text("DiscordPhotoSaved")
                        : notification.Count == 1
                            ? SentoryLocalization.Text("DiscordUrlSaved")
                            : SentoryLocalization.Format(
                                "DiscordUrlsSavedFormat",
                                notification.Count)
                    : notification.Kind == ContentKind.Collection
                    ? SentoryLocalization.Text("InputCollectionSaved")
                    : notification.Kind == Sentory.Core.ContentKind.Image
                    ? SentoryLocalization.Text("InputPhotoSaved")
                    : notification.Count == 1
                        ? SentoryLocalization.Text("InputUrlSaved")
                        : SentoryLocalization.Format(
                            "InputUrlsSavedFormat",
                            notification.Count),
                Forms.ToolTipIcon.Info);

            if (_galleryWindow is { IsLoaded: true })
            {
                _ = RefreshGalleryAfterCaptureAsync(notification);
            }

            if (notification.Kind is ContentKind.Url or ContentKind.Collection)
            {
                WakeLinkPreviewWorker();
            }

            if (notification.Kind is ContentKind.Image or ContentKind.Collection)
            {
                WakeOcrWorker();
            }
        });
    }

    private async Task RefreshGalleryAfterCaptureAsync(
        CaptureNotification notification)
    {
        if (_galleryWindow is not { IsLoaded: true } gallery)
        {
            return;
        }

        _diagnosticsLog?.Write(
            "gallery-refresh-started",
            $"source={notification.SourceApp}, kind={notification.Kind}");
        await gallery.RefreshAfterCaptureAsync();
        _diagnosticsLog?.Write(
            "gallery-refresh-completed",
            $"source={notification.SourceApp}, kind={notification.Kind}");
    }

    private void OnCaptureIssueDetected(
        object? sender,
        CaptureRuntimeIssue issue)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var discordUnavailable = string.Equals(
                issue.Code,
                "discord-detection-unavailable",
                StringComparison.Ordinal);
            if (discordUnavailable && _discordSupportEnabled)
            {
                SetDiscordRepairNeeded(true, persistPrepared: false);
            }
            _lastRuntimeIssueCode = issue.Code;
            _lastRuntimeIssue = discordUnavailable
                ? SentoryLocalization.Text("DiscordRecoveryIssue")
                : SentoryLocalization.Text("CaptureIssue");
            _galleryWindow?.SetRuntimeIssue(_lastRuntimeIssue);
            _diagnosticsLog?.Write(
                issue.Code,
                issue.UserMessage);
            SetStatus(
                discordUnavailable
                    ? SentoryLocalization.Text("StatusDiscordRecovery")
                    : SentoryLocalization.Text("StatusCaptureIssue"),
                discordUnavailable
                    ? SentoryLocalization.Text("TrayDiscordRecovery")
                    : SentoryLocalization.Text("TrayDetecting"));
            _trayIcon?.ShowBalloonTip(
                discordUnavailable ? 4500 : 2500,
                "Sentory",
                discordUnavailable
                    ? SentoryLocalization.Text("ApplyDiscordRecovery")
                    : SentoryLocalization.Text("CaptureIssue"),
                Forms.ToolTipIcon.Warning);
        });
    }

    private void OnCaptureStatusChanged(
        object? sender,
        CaptureRuntimeStatus status)
    {
        if (status.SourceApp != SourceApp.Discord ||
            !_discordSupportEnabled)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            _discordDetectionState = status.State;
            _galleryWindow?.SetDiscordDetectionState(status.State);
            if (DiscordStartupPreparationPolicy.ShouldClearRuntimeIssue(
                    _lastRuntimeIssueCode,
                    status.State))
            {
                _lastRuntimeIssueCode = null;
                _lastRuntimeIssue = null;
                _galleryWindow?.SetRuntimeIssue(null);
            }
            if (_discordSupportEnabled)
            {
                var repairNeeded =
                    DiscordStartupPreparationPolicy.ResolveRepairNeeded(
                        _discordRepairNeeded,
                        status.State);
                var persistPrepared = status.State switch
                {
                    CaptureRuntimeState.Ready => true,
                    CaptureRuntimeState.ReconnectRequired => false,
                    _ => (bool?)null
                };
                SetDiscordRepairNeeded(
                    repairNeeded,
                    persistPrepared);
            }

            if (status.State == CaptureRuntimeState.ReconnectRequired)
            {
                _ = PromptAutomaticDiscordRestartAsync();
            }

            if (_runtime?.IsPaused != true &&
                _observedDiscordProcessId.HasValue)
            {
                SetStatus(
                    SentoryLocalization.Format(
                        "StatusFormat",
                        DiscordDetectionPresentation.GetLabel(status.State)),
                    $"Sentory - {DiscordDetectionPresentation.GetLabel(status.State)}");
            }
        });
    }

    private async Task RepairDiscordConnectionAsync()
    {
        if (_discordRepairBusy || !_discordSupportEnabled)
        {
            return;
        }

        var dark = GetSavedDarkTheme();
        if (!SentoryDialogWindow.Confirm(
                _galleryWindow,
                SentoryLocalization.Text("ReconnectConfirmHeading"),
                SentoryLocalization.Text("ReconnectConfirmMessage"),
                SentoryLocalization.Text("Restart"),
                dark))
        {
            return;
        }

        _discordRepairBusy = true;
        await RestartDiscordConnectionCoreAsync();
    }

    private async Task RestartDiscordConnectionCoreAsync()
    {
        _observedDiscordAccessibilityArgumentState =
            DiscordAccessibilityArgumentState.Unknown;
        ApplyDiscordRestartUiState(
            DiscordStartupPreparationPolicy.RestartStarted);
        SetStatus(
            _runtime?.IsPaused == true
                ? SentoryLocalization.Text("StatusPaused")
                : SentoryLocalization.Text("StatusDetecting"),
            _runtime?.IsPaused == true
                ? SentoryLocalization.Text("TrayPaused")
                : SentoryLocalization.Text("TrayDetecting"));

        try
        {
            await _discordLauncher.RestartAsync();
            SetDiscordRepairNeeded(false, persistPrepared: true);
            if (_runtime is ICaptureRuntimeRecoveryController controller)
            {
                controller.RequestRecovery(SourceApp.Discord);
            }
            _trayIcon?.ShowBalloonTip(
                3500,
                "Sentory",
                SentoryLocalization.Text("DiscordRestarted"),
                Forms.ToolTipIcon.Info);
        }
        catch (Exception exception)
            when (exception is IOException or
                  InvalidOperationException or
                  System.ComponentModel.Win32Exception or
                  UnauthorizedAccessException)
        {
            ApplyDiscordRestartUiState(
                DiscordStartupPreparationPolicy.RestartFailed,
                persistPrepared: false);
            _diagnosticsLog?.Write(
                "discord-repair-failed",
                "Discord connection repair failed",
                exception);
            _trayIcon?.ShowBalloonTip(
                4000,
                "Sentory",
                SentoryLocalization.Text("DiscordRepairFailed"),
                Forms.ToolTipIcon.Warning);
        }
        finally
        {
            _discordRepairBusy = false;
        }
    }

    private void ApplyDiscordRestartUiState(
        DiscordRestartUiState state,
        bool? persistPrepared = null)
    {
        _discordDetectionState = state.DetectionState;
        _galleryWindow?.SetDiscordDetectionState(
            state.DetectionState);
        SetDiscordRepairNeeded(
            state.RepairNeeded,
            persistPrepared);
    }

    private async Task PromptAutomaticDiscordRestartAsync()
    {
        if (_discordRestartPromptActive)
        {
            return;
        }

        var processId = _discordLauncher.GetMainProcessId();
        if (!DiscordAutomaticRestartPolicy.ShouldPrompt(
                _discordSupportEnabled,
                _runtime?.IsPaused == true,
                _discordRepairBusy,
                _discordDetectionState,
                _observedDiscordAccessibilityArgumentState,
                processId,
                _automaticRestartPromptedProcessId))
        {
            return;
        }

        _discordRestartPromptActive = true;
        _automaticRestartPromptedProcessId = processId;
        try
        {
            _diagnosticsLog?.Write(
                "discord-auto-restart-prompted",
                $"processId={processId} countdownSeconds=15");
            var restart = SentoryDialogWindow.ConfirmWithCountdown(
                _galleryWindow,
                SentoryLocalization.Text("AutomaticReconnectHeading"),
                seconds => SentoryLocalization.Format(
                    "AutomaticReconnectMessageFormat",
                    seconds),
                SentoryLocalization.Text("RestartNow"),
                GetSavedDarkTheme(),
                countdownSeconds: 15);
            if (!restart)
            {
                _diagnosticsLog?.Write(
                    "discord-auto-restart-cancelled",
                    $"processId={processId}");
                return;
            }

            if (_discordLauncher.GetMainProcessId() != processId)
            {
                _diagnosticsLog?.Write(
                    "discord-auto-restart-skipped",
                    $"processId={processId} reason=process-changed");
                if (_runtime is ICaptureRuntimeRecoveryController controller)
                {
                    controller.RequestRecovery(SourceApp.Discord);
                }
                return;
            }

            _discordRepairBusy = true;
            _diagnosticsLog?.Write(
                "discord-auto-restart-started",
                $"processId={processId}");
            await RestartDiscordConnectionCoreAsync();
        }
        finally
        {
            _discordRestartPromptActive = false;
        }
    }

    private void SetDiscordRepairNeeded(
        bool needed,
        bool? persistPrepared = null)
    {
        _discordRepairNeeded = needed;
        _galleryWindow?.SetDiscordRepairNeeded(needed);
        if (_settingsStore is null || persistPrepared is null)
        {
            return;
        }

        try
        {
            var settings = _settingsStore.Load();
            settings.DiscordAccessibilityPrepared =
                persistPrepared.Value;
            _settingsStore.Save(settings);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            _diagnosticsLog?.Write(
                "settings-save-failed",
                "Discord preparation state could not be saved",
                exception);
        }
    }

    private void SetStatus(string status, string trayText)
    {
        _statusText = status;

        if (_trayIcon is not null)
        {
            _trayIcon.Text = trayText;
        }
    }

    private void OpenDataFolder()
    {
        _paths.EnsureDirectories();
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { _paths.RootDirectory },
            UseShellExecute = true
        });
    }

    private void OpenGallery()
    {
        if (_repository is null ||
            _settingsStore is null ||
            _linkPreviewFetcher is null)
        {
            return;
        }

        if (_galleryWindow is null)
        {
            _galleryWindow = new GalleryWindow(
                _repository,
                _paths,
                _settingsStore,
                _linkPreviewFetcher,
                _syncStatusTracker);
            _galleryWindow.DiscordRepairRequested += async (_, _) =>
                await RepairDiscordConnectionAsync();
            _galleryWindow.UpdateInstallRequested += UpdateInstallRequested;
            _galleryWindow.ManualUpdateCheckRequested +=
                CheckForUpdatesManuallyAsync;
            _galleryWindow.MessengerSupportChanged +=
                ApplyMessengerSupportSetting;
            _galleryWindow.StartupChanged +=
                SynchronizeDiscordStartupRegistrationAsync;
            _galleryWindow.LanguageChanged += (_, _) => UpdatePauseUi();
            _galleryWindow.AutoFavoriteSettingsChanged +=
                ApplyAutomaticFavoriteSettings;
            _galleryWindow.SyncConfigurationChanged += (_, _) =>
                WakeSyncWorker();
            _galleryWindow.ItemMetadataChanged += (_, _) =>
                WakeSyncWorker();
            _galleryWindow.SetDiscordRepairNeeded(
                _discordSupportEnabled && _discordRepairNeeded);
            _galleryWindow.SetDiscordDetectionState(
                _discordDetectionState);
            _galleryWindow.SetDiscordProcessRunning(
                _observedDiscordProcessId.HasValue);
            _galleryWindow.SetMessengerSupportState(
                _discordSupportEnabled,
                _kakaoSupportEnabled,
                _slackSupportEnabled,
                _whatsAppSupportEnabled,
                _telegramSupportEnabled,
                _lineSupportEnabled,
                _weChatSupportEnabled);
            _galleryWindow.SetDetectionPaused(
                _runtime?.IsPaused == true);
            _galleryWindow.SetRuntimeIssue(_lastRuntimeIssue);
            _galleryWindow.SetAvailableUpdate(
                _availableUpdate?.Version,
                _updateInstallationInProgress);
            MainWindow = _galleryWindow;
            _galleryWindow.Closed += (_, _) =>
            {
                _galleryWindow.AutoFavoriteSettingsChanged -=
                    ApplyAutomaticFavoriteSettings;
                if (ReferenceEquals(MainWindow, _galleryWindow))
                {
                    MainWindow = null;
                }

                _galleryWindow = null;
            };
            _galleryWindow.ShowInTaskbar = true;
            _galleryWindow.Show();
            _galleryWindow.Activate();
            Dispatcher.BeginInvoke(
                EnsureTrayMenu,
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }

        if (_galleryWindow.WindowState == WindowState.Minimized)
        {
            _galleryWindow.WindowState = WindowState.Normal;
        }

        MainWindow = _galleryWindow;
        _galleryWindow.ShowInTaskbar = true;
        _galleryWindow.Show();
        _galleryWindow.Activate();
    }

    private void ApplyAutomaticFavoriteSettings(
        bool enabled,
        int usageThreshold)
    {
        if (_repository is SqliteCaptureRepository captureRepository)
        {
            captureRepository.ConfigureAutomaticFavorites(
                enabled,
                usageThreshold);
        }

        WakeSyncWorker();
    }

    private async Task RunMaintenanceLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
                if (_settingsStore is not null)
                {
                    var settings = _settingsStore.Load();
                    SynchronizeDiscordStartupRegistration(
                        settings.DiscordSupportEnabled,
                        GetStartupEnabled());
                }
                await ApplyAutomaticCleanupAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunDiscordConnectionMonitorAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var processId = _discordSupportEnabled
                    ? _discordLauncher.GetMainProcessId()
                    : null;
                if (processId != _observedDiscordProcessId)
                {
                    var previousProcessId = _observedDiscordProcessId;
                    _observedDiscordProcessId = processId;
                    _galleryWindow?.SetDiscordProcessRunning(
                        processId.HasValue);
                    if (!processId.HasValue)
                    {
                        UpdatePauseUi();
                    }
                    if (_automaticRestartPromptedProcessId != processId)
                    {
                        _automaticRestartPromptedProcessId = null;
                    }
                    _observedDiscordAccessibilityArgumentState =
                        DiscordAccessibilityArgumentState.Unknown;
                    _diagnosticsLog?.Write(
                        "discord-process-changed",
                        $"previous={previousProcessId?.ToString() ?? "none"} current={processId?.ToString() ?? "none"}");

                    if (processId is int currentProcessId)
                    {
                        var argumentState = await Task.Run(
                            () => _discordLauncher
                                .GetAccessibilityArgumentState(
                                    currentProcessId),
                            cancellationToken);
                        if (_discordLauncher.GetMainProcessId() !=
                            currentProcessId)
                        {
                            continue;
                        }

                        _observedDiscordAccessibilityArgumentState =
                            argumentState;
                        _diagnosticsLog?.Write(
                            "discord-process-accessibility-argument",
                            $"processId={currentProcessId} state={argumentState}");
                        if (DiscordAutomaticRestartPolicy
                            .ShouldPromptImmediately(argumentState))
                        {
                            _discordDetectionState =
                                CaptureRuntimeState.ReconnectRequired;
                            _galleryWindow?.SetDiscordDetectionState(
                                _discordDetectionState);
                            SetDiscordRepairNeeded(
                                true,
                                persistPrepared: false);
                            _ = PromptAutomaticDiscordRestartAsync();
                        }
                        else if (_runtime is
                            ICaptureRuntimeRecoveryController controller)
                        {
                            controller.RequestRecovery(SourceApp.Discord);
                        }
                    }
                    else if (_runtime is
                        ICaptureRuntimeRecoveryController controller)
                    {
                        controller.RequestRecovery(SourceApp.Discord);
                    }
                }

                var delay =
                    DiscordAutomaticRestartPolicy.GetProcessCheckInterval(
                        _discordSupportEnabled,
                        _discordDetectionState);
                if (processId is int currentMainProcessId)
                {
                    _ = await _discordLauncher
                        .WaitForMainProcessExitAsync(
                            currentMainProcessId,
                            delay,
                            cancellationToken);
                }
                else
                {
                    await Task.Delay(delay, cancellationToken);
                }
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
        if (_linkPreviewService is null)
        {
            return;
        }

        try
        {
            while (true)
            {
                var updated = 0;
                try
                {
                    updated = await _linkPreviewService.EnrichBatchAsync(
                        4,
                        DateTimeOffset.UtcNow.AddDays(-30),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _diagnosticsLog?.Write(
                        "link-preview-failed",
                        "Link preview enrichment failed",
                        exception);
                }

                if (updated > 0)
                {
                    Task refreshTask = Task.CompletedTask;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (_galleryWindow is { IsLoaded: true })
                        {
                            refreshTask = _galleryWindow.RefreshAsync();
                        }
                    });
                    await refreshTask;
                }

                if (updated == 4)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(1),
                        cancellationToken);
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

    private void WakeLinkPreviewWorker()
    {
        if (_linkPreviewWakeSignal.CurrentCount == 0)
        {
            try
            {
                _linkPreviewWakeSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    private async Task RunOcrLoopAsync(
        CancellationToken cancellationToken)
    {
        if (_ocrService is null)
        {
            return;
        }

        try
        {
            while (true)
            {
                OcrEnrichmentBatchResult result;
                try
                {
                    result = await _ocrService.EnrichBatchAsync(
                        1,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _diagnosticsLog?.Write(
                        "image-ocr-failed",
                        "Image OCR enrichment failed",
                        exception);
                    result = new OcrEnrichmentBatchResult(0, 0);
                }

                if (result.Updated > 0)
                {
                    Task refreshTask = Task.CompletedTask;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (_galleryWindow is { IsLoaded: true })
                        {
                            refreshTask = _galleryWindow.RefreshAsync();
                        }
                    });
                    await refreshTask;
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
        if (_ocrWakeSignal.CurrentCount == 0)
        {
            try
            {
                _ocrWakeSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    private async Task RunSyncLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
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

    private async Task RunConfiguredSyncOnceAsync(
        CancellationToken cancellationToken)
    {
        if (_settingsStore is null || _repository is null)
        {
            return;
        }

        var settings = _settingsStore.Load();
        if (!settings.SyncEnabled ||
            settings.SyncFolderPath is not { Length: > 0 }
                selectedDirectory ||
            settings.SyncDeviceId is not { } deviceId ||
            !Sentory.Core.Sync.SyncDeviceIdentity.IsValid(deviceId))
        {
            _syncStatusTracker.Update(
                SyncRuntimeState.Disabled,
                DateTimeOffset.UtcNow);
            return;
        }

        _syncStatusTracker.Update(
            settings.SyncStorageVersion <
                    SentorySettings.CurrentSyncStorageVersion ||
                settings.SyncMigrationDeviceId is not null
                ? SyncRuntimeState.Migrating
                : SyncRuntimeState.Syncing,
            DateTimeOffset.UtcNow);
        try
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
                deviceId = migration.DeviceId;
                _diagnosticsLog?.Write(
                    "cloud-sync-storage-migrated",
                    $"version={SentorySettings.CurrentSyncStorageVersion}, legacyProjected={migration.LegacyProjected}");
                _syncStatusTracker.Update(
                    SyncRuntimeState.Syncing,
                    DateTimeOffset.UtcNow);
            }
            if (migration.DeviceBindingReset)
            {
                _diagnosticsLog?.Write(
                    "cloud-sync-device-binding-reset",
                    "Requeued local items after repairing a mismatched sync device binding");
            }

            var result = await new LocalFolderSyncRuntimeService(
                _paths,
                _repository,
                _settingsStore,
                () => _syncStatusTracker.Update(
                    SyncRuntimeState.Recovering,
                    DateTimeOffset.UtcNow)).RunOnceAsync(
                    deviceId,
                    selectedDirectory,
                    cancellationToken);
            if (result.DeviceBindingReset &&
                !migration.DeviceBindingReset)
            {
                _diagnosticsLog?.Write(
                    "cloud-sync-device-binding-reset",
                    "Requeued local items after repairing a mismatched sync device binding during runtime initialization");
            }
            if (result.StoreReset)
            {
                _diagnosticsLog?.Write(
                    "cloud-sync-store-rebuilt",
                    "Rebuilt a deleted or replaced cloud sync store from the local gallery");
            }
            if (result.AssetRepair.Repaired > 0 ||
                result.AssetRepair.MissingLocal > 0)
            {
                _diagnosticsLog?.Write(
                    "cloud-sync-assets-repaired",
                    $"repaired={result.AssetRepair.Repaired}, missingLocal={result.AssetRepair.MissingLocal}");
            }
            var succeededAt = DateTimeOffset.UtcNow;
            _syncStatusTracker.Update(
                SyncRuntimeState.Succeeded,
                succeededAt,
                succeededAt);
            _diagnosticsLog?.Write(
                "cloud-sync-completed",
                $"storeReset={result.StoreReset}, repairedAssets={result.AssetRepair.Repaired}, exported={result.Export.Exported}, metadataExported={result.Metadata.Exported}, uploaded={result.Cycle.Transfer.Uploaded + result.Publish.Uploaded}, downloaded={result.Cycle.Transfer.Downloaded + result.Publish.Downloaded}, projected={result.Cycle.Projection.Projected}, pending={result.Cycle.Projection.Pending}, metadataProjected={result.Metadata.Projected}");

            if (result.Metadata.SettingsChanged)
            {
                var synchronizedSettings = _settingsStore.Load();
                await Dispatcher.InvokeAsync(() =>
                    _galleryWindow?.ApplySyncedAutoFavoriteSettings(
                        synchronizedSettings.AutoFavoriteEnabled,
                        synchronizedSettings.AutoFavoriteCopyThreshold));
            }

            if (result.Cycle.Projection.Projected > 0 ||
                result.Metadata.Projected > 0 ||
                migration.LegacyProjected > 0)
            {
                Task refreshTask = Task.CompletedTask;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_galleryWindow is { IsLoaded: true })
                    {
                        refreshTask = _galleryWindow.RefreshAsync();
                    }
                });
                await refreshTask;
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
        catch (Sentory.Core.Sync.SyncStoreUnavailableException exception)
        {
            _syncStatusTracker.Update(
                SyncRuntimeState.FolderUnavailable,
                DateTimeOffset.UtcNow);
            _diagnosticsLog?.Write(
                "cloud-sync-folder-unavailable",
                "Cloud sync folder is unavailable",
                exception);
        }
        catch (InvalidDataException exception)
        {
            _syncStatusTracker.Update(
                SyncRuntimeState.InvalidData,
                DateTimeOffset.UtcNow);
            _diagnosticsLog?.Write(
                "cloud-sync-invalid-data",
                "Cloud sync data is invalid",
                exception);
        }
        catch (Exception exception)
        {
            _syncStatusTracker.Update(
                SyncRuntimeState.Failed,
                DateTimeOffset.UtcNow);
            _diagnosticsLog?.Write(
                "cloud-sync-failed",
                "Cloud sync failed",
                exception);
        }
    }

    private void WakeSyncWorker()
    {
        if (_syncWakeSignal.CurrentCount == 0)
        {
            try
            {
                _syncWakeSignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    private async Task ApplyAutomaticCleanupAsync(
        CancellationToken cancellationToken = default)
    {
        if (_repository is null || _settingsStore is null)
        {
            return;
        }

        try
        {
            var settings = _settingsStore.Load();
            if (settings.AutoCleanupDays == 0)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (settings.LastAutoCleanupAt is { } lastCleanup &&
                now - lastCleanup < TimeSpan.FromHours(24))
            {
                return;
            }

            var result = await _repository.CleanupAsync(
                now.AddDays(-settings.AutoCleanupDays),
                cancellationToken);
            settings.LastAutoCleanupAt = now;
            _settingsStore.Save(settings);
            if (result.Deleted.TotalItems > 0)
            {
                _trayIcon?.ShowBalloonTip(
                    2600,
                    SentoryLocalization.Text("AutoCleanupTitle"),
                    SentoryLocalization.Format(
                        "AutoCleanupCompletedFormat",
                        result.Deleted.TotalItems),
                    Forms.ToolTipIcon.Info);
                if (_galleryWindow is { IsLoaded: true })
                {
                    await _galleryWindow.RefreshAsync();
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _lastRuntimeIssueCode = "auto-cleanup-failed";
            _lastRuntimeIssue =
                SentoryLocalization.Text("AutoCleanupFailedNextTime");
            _galleryWindow?.SetRuntimeIssue(_lastRuntimeIssue);
            _diagnosticsLog?.Write(
                "auto-cleanup-failed",
                _lastRuntimeIssue,
                exception);
            _trayIcon?.ShowBalloonTip(
                2600,
                SentoryLocalization.Text("AutoCleanupTitle"),
                SentoryLocalization.Text("AutoCleanupFailedNextTime"),
                Forms.ToolTipIcon.Warning);
        }
    }

    private async Task ShutdownRuntimeAsync()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        var shutdownTimer = Stopwatch.StartNew();
        _diagnosticsLog?.Write(
            "shutdown-started",
            "Sentory shutdown started");
        HideUserInterfaceForShutdown();
        _maintenanceCancellation.Cancel();
        if (_maintenanceTask is not null)
        {
            await _maintenanceTask;
            _maintenanceTask = null;
        }
        if (_discordConnectionMonitorTask is not null)
        {
            await _discordConnectionMonitorTask;
            _discordConnectionMonitorTask = null;
        }
        if (_linkPreviewTask is not null)
        {
            await _linkPreviewTask;
            _linkPreviewTask = null;
        }
        if (_ocrTask is not null)
        {
            await _ocrTask;
            _ocrTask = null;
        }
        if (_syncTask is not null)
        {
            await _syncTask;
            _syncTask = null;
        }
        _linkPreviewFetcher?.Dispose();
        _linkPreviewFetcher = null;
        _linkPreviewService = null;
        _ocrService = null;
        _ocrRecognizer?.Dispose();
        _ocrRecognizer = null;
        _kakaoDropOverlay?.Dispose();
        _kakaoDropOverlay = null;
        _discordDropOverlay?.Dispose();
        _discordDropOverlay = null;
        _slackDropOverlay?.Dispose();
        _slackDropOverlay = null;
        _whatsAppDropOverlay?.Dispose();
        _whatsAppDropOverlay = null;
        _telegramDropOverlay?.Dispose();
        _telegramDropOverlay = null;
        _lineDropOverlay?.Dispose();
        _lineDropOverlay = null;
        if (_runtime is not null)
        {
            _runtime.Captured -= OnCaptured;
            _runtime.IssueDetected -= OnCaptureIssueDetected;
            if (_runtime is ICaptureRuntimeStatusSource statusSource)
            {
                statusSource.StatusChanged -= OnCaptureStatusChanged;
            }
            await _runtime.DisposeAsync();
            _runtime = null;
        }

        shutdownTimer.Stop();
        _diagnosticsLog?.Write(
            "shutdown-completed",
            $"Sentory shutdown completed in {shutdownTimer.ElapsedMilliseconds} ms");
    }

    private void HideUserInterfaceForShutdown()
    {
        _trayMenuWindow?.CloseForShutdown();
        _trayMenuWindow = null;
        _galleryWindow?.PrepareForApplicationShutdown();
        _galleryWindow?.Close();
        _galleryWindow = null;
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayIconImage?.Dispose();
        _trayIconImage = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_shuttingDown)
        {
            ShutdownRuntimeAsync().GetAwaiter().GetResult();
        }

        if (_ownsMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        _openGalleryRegistration?.Unregister(null);
        _openGalleryRegistration = null;
        _openGalleryEvent?.Dispose();
        _openGalleryEvent = null;
        _maintenanceCancellation.Dispose();
        _linkPreviewWakeSignal.Dispose();
        _ocrWakeSignal.Dispose();
        _syncWakeSignal.Dispose();
        _updateClient.Dispose();
        _updateCheckGate.Dispose();
        base.OnExit(e);
    }
}
