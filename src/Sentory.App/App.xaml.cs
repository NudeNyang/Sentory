using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Sentory.Core;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Links;
using Sentory.Infrastructure.Ocr;
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
    private readonly WindowsStartupManager _startupManager = new();
    private readonly DiscordAccessibilityLauncher _discordLauncher = new();
    private bool _discordSupportEnabled = true;
    private bool _kakaoSupportEnabled = true;
    private bool _discordRepairNeeded;
    private bool _discordRepairBusy;
    private CaptureRuntimeState _discordDetectionState =
        CaptureRuntimeState.Connecting;
    private bool _shuttingDown;
    private string? _lastRuntimeIssue;
    private readonly GitHubReleaseUpdateClient _updateClient = new();
    private ReleaseUpdate? _availableUpdate;
    private string? _downloadedUpdatePackage;
    private bool _updateInstallationInProgress;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (PortableUpdateApplier.IsApplyCommand(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await PortableUpdateApplier.RunAsync(e.Args));
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
            }

            PrepareDiscordDefault();
            _repository = new SqliteCaptureRepository(_paths);
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
            _runtime = new CompositeCaptureRuntime(
                (SourceApp.KakaoTalk, kakaoRuntime),
                (SourceApp.Discord, discordRuntime));
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
            _ocrTask = Task.Run(() => RunOcrLoopAsync(
                _maintenanceCancellation.Token));
            ApplyRuntimeSourceSettings();
            _runtime.Start();
            _kakaoDropOverlay.Start();
            _discordDropOverlay.Start();
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
            if (_settingsStore is null) return;
            var settings = _settingsStore.Load();
            var now = DateTimeOffset.UtcNow;
            if (settings.LastUpdateCheckAt is { } checkedAt &&
                now - checkedAt < TimeSpan.FromHours(6))
            {
                return;
            }

            settings.LastUpdateCheckAt = now;
            _settingsStore.Save(settings);
            var packageKind = File.Exists(Path.Combine(
                AppContext.BaseDirectory, "unins000.exe"))
                ? UpdatePackageKind.Installer
                : UpdatePackageKind.Portable;
            var currentVersion = GetApplicationVersion();
            var update = await _updateClient.CheckAsync(
                currentVersion,
                RuntimeInformation.ProcessArchitecture,
                packageKind,
                cancellationToken);
            if (update is null || cancellationToken.IsCancellationRequested) return;

            var directory = Path.Combine(
                Path.GetTempPath(), "Sentory", "downloads", update.Version);
            _diagnosticsLog?.Write(
                "update-download-started",
                $"Downloading Sentory {update.Version} update");
            var package = await _updateClient.DownloadAsync(
                update,
                directory,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            _availableUpdate = update;
            _downloadedUpdatePackage = package;
            settings.LastUpdateCheckAt = null;
            _settingsStore.Save(settings);
            _diagnosticsLog?.Write(
                "update-download-completed",
                $"Sentory {update.Version} update is ready");

            await await Dispatcher.InvokeAsync(async () =>
            {
                _galleryWindow?.SetAvailableUpdate(update.Version);
                await PromptAndInstallUpdateAsync(
                    update,
                    package,
                    cancellationToken);
            });
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
            _maintenanceCancellation.Token);
    }

    private async Task PromptAndInstallUpdateAsync(
        ReleaseUpdate update,
        string package,
        CancellationToken cancellationToken)
    {
        if (_updateInstallationInProgress || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var accepted = SentoryDialogWindow.Confirm(
            _galleryWindow,
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
                var startInfo = new ProcessStartInfo
                {
                    FileName = package,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(package)
                };
                startInfo.ArgumentList.Add("/SILENT");
                startInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
                startInfo.ArgumentList.Add("/CLOSEAPPLICATIONS");
                startInfo.ArgumentList.Add("/NORESTART");
                startInfo.ArgumentList.Add("/SENTORYUPDATE=1");
                Process.Start(startInfo);
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

    private static string GetApplicationVersion()
    {
        var value = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0";
        return value.Split('+', 2)[0];
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
        _trayMenuWindow?.Close();
        var isDarkTheme = GetSavedDarkTheme();
        var menu = new TrayMenuWindow(
            _statusText,
            _runtime?.IsPaused == true,
            GetStartupEnabled(),
            _discordSupportEnabled,
            _discordDetectionState,
            _discordRepairNeeded,
            isDarkTheme);
        _trayMenuWindow = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trayMenuWindow, menu))
            {
                _trayMenuWindow = null;
            }
        };
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

        var cursor = Forms.Cursor.Position;
        menu.WindowStartupLocation = WindowStartupLocation.Manual;
        menu.Left = cursor.X;
        menu.Top = cursor.Y;
        menu.Show();

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

    private void PrepareDiscordDefault()
    {
        if (_settingsStore is null)
        {
            return;
        }

        var settings = _settingsStore.Load();
        _discordSupportEnabled = settings.DiscordSupportEnabled;
        _kakaoSupportEnabled = settings.KakaoTalkSupportEnabled;
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
                _kakaoSupportEnabled);
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
            _kakaoSupportEnabled);
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

        if (sourceApp != SourceApp.KakaoTalk)
        {
            return;
        }

        _kakaoSupportEnabled = enabled;
        ApplyRuntimeSourceSettings();
        _galleryWindow?.SetMessengerSupportState(
            _discordSupportEnabled,
            _kakaoSupportEnabled);
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
    }

    private void ApplyPauseState()
    {
        if (_runtime is null)
        {
            return;
        }

        _runtime.IsPaused = !_runtime.IsPaused;
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

        var statusKey = (_discordSupportEnabled, _kakaoSupportEnabled) switch
        {
            (true, true) => "StatusDetecting",
            (true, false) => "StatusDetectingDiscord",
            (false, true) => "StatusDetectingKakao",
            _ => "StatusDetectionDisabled"
        };
        SetStatus(
            SentoryLocalization.Text(statusKey),
            SentoryLocalization.Text(
                _discordSupportEnabled || _kakaoSupportEnabled
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
        Dispatcher.BeginInvoke(() =>
        {
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

            if (_runtime?.IsPaused != true)
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
        try
        {
            await _discordLauncher.RestartAsync();
            SetDiscordRepairNeeded(false, persistPrepared: true);
            _discordDetectionState = CaptureRuntimeState.Connecting;
            _galleryWindow?.SetDiscordDetectionState(
                _discordDetectionState);
            if (_runtime is ICaptureRuntimeRecoveryController controller)
            {
                controller.RequestRecovery(SourceApp.Discord);
            }
            SetStatus(
                _runtime?.IsPaused == true
                    ? SentoryLocalization.Text("StatusPaused")
                    : SentoryLocalization.Text("StatusDetecting"),
                _runtime?.IsPaused == true
                    ? SentoryLocalization.Text("TrayPaused")
                    : SentoryLocalization.Text("TrayDetecting"));
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
                _linkPreviewFetcher);
            _galleryWindow.DiscordRepairRequested += async (_, _) =>
                await RepairDiscordConnectionAsync();
            _galleryWindow.UpdateInstallRequested += UpdateInstallRequested;
            _galleryWindow.MessengerSupportChanged +=
                ApplyMessengerSupportSetting;
            _galleryWindow.LanguageChanged += (_, _) => UpdatePauseUi();
            _galleryWindow.SetDiscordRepairNeeded(
                _discordSupportEnabled && _discordRepairNeeded);
            _galleryWindow.SetDiscordDetectionState(
                _discordDetectionState);
            _galleryWindow.SetMessengerSupportState(
                _discordSupportEnabled,
                _kakaoSupportEnabled);
            _galleryWindow.SetRuntimeIssue(_lastRuntimeIssue);
            _galleryWindow.SetAvailableUpdate(
                _availableUpdate?.Version,
                _updateInstallationInProgress);
            MainWindow = _galleryWindow;
            _galleryWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(MainWindow, _galleryWindow))
                {
                    MainWindow = null;
                }

                _galleryWindow = null;
            };
            _galleryWindow.ShowInTaskbar = true;
            _galleryWindow.Show();
            _galleryWindow.Activate();
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

    private async Task RunMaintenanceLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
                await ApplyAutomaticCleanupAsync(cancellationToken);
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
        _trayMenuWindow?.Close();
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
        _updateClient.Dispose();
        base.OnExit(e);
    }
}
