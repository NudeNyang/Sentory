using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using Sentory.Core;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Links;
using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;
using Forms = System.Windows.Forms;

namespace Sentory.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName =
        "Local\\Sentory.Desktop.Singleton";
    private const string InstallationVerificationArgument =
        "--verify-installation";

    private readonly SentoryDataPaths _paths =
        SentoryDataPaths.FromEnvironmentOrCurrentUser(
            Environment.GetEnvironmentVariable("SENTORY_DATA_DIR"));
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _trayIconImage;
    private TrayMenuWindow? _trayMenuWindow;
    private string _statusText = "시작 중...";
    private ICaptureRepository? _repository;
    private SentorySettingsStore? _settingsStore;
    private SentoryDiagnosticsLog? _diagnosticsLog;
    private ICaptureRuntime? _runtime;
    private GalleryWindow? _galleryWindow;
    private readonly CancellationTokenSource _maintenanceCancellation = new();
    private Task? _maintenanceTask;
    private readonly SemaphoreSlim _linkPreviewWakeSignal = new(0, 1);
    private LinkPreviewFetcher? _linkPreviewFetcher;
    private LinkPreviewEnrichmentService? _linkPreviewService;
    private Task? _linkPreviewTask;
    private readonly WindowsStartupManager _startupManager = new();
    private readonly DiscordAccessibilityLauncher _discordLauncher = new();
    private bool _discordSupportEnabled = true;
    private bool _discordRepairNeeded;
    private bool _discordRepairBusy;
    private CaptureRuntimeState _discordDetectionState =
        CaptureRuntimeState.Connecting;
    private bool _shuttingDown;
    private string? _lastRuntimeIssue;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _settingsStore = new SentorySettingsStore(_paths);
        SentoryLocalization.Apply(
            Resources,
            _settingsStore.Load().Language);
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
                SentoryDialogWindow.ShowMessage(
                    null,
                    SentoryLocalization.Text("AlreadyRunningHeading"),
                    SentoryLocalization.Text("AlreadyRunningMessage"),
                    GetSavedDarkTheme());
                Shutdown();
                return;
            }
        }

        try
        {
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

            var acceptInjectedInput = string.Equals(
                Environment.GetEnvironmentVariable(
                    "SENTORY_ACCEPT_INJECTED_INPUT"),
                "1",
                StringComparison.Ordinal);
            _runtime = new CompositeCaptureRuntime(
                new KakaoCaptureRuntime(
                    _repository,
                    acceptInjectedInput),
                new DiscordCaptureRuntime(
                    _repository,
                    acceptInjectedInput));
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
            _runtime.Start();
            UpdatePauseUi();
            if (e.Args.Contains("--gallery", StringComparer.OrdinalIgnoreCase))
            {
                OpenGallery();
            }
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

    private void ShowTrayMenu()
    {
        _trayMenuWindow?.Close();
        var isDarkTheme = _settingsStore?.Load().IsDarkTheme == true;
        var menu = new TrayMenuWindow(
            _statusText,
            _runtime?.IsPaused == true,
            GetStartupEnabled(),
            _discordSupportEnabled,
            _discordDetectionState,
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
        if (!_discordSupportEnabled || !_discordLauncher.IsInstalled)
        {
            return;
        }

        try
        {
            if (!_discordLauncher.IsRunning())
            {
                _discordLauncher.Start();
                settings.DiscordAccessibilityPrepared = true;
                _settingsStore.Save(settings);
            }
            else if (!settings.DiscordAccessibilityPrepared)
            {
                _discordRepairNeeded = true;
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
            return;
        }

        try
        {
            if (_discordLauncher.IsInstalled &&
                !_discordLauncher.IsRunning())
            {
                _discordLauncher.Start();
                settings.DiscordAccessibilityPrepared = true;
                _discordRepairNeeded = false;
            }
            else if (_discordLauncher.IsInstalled &&
                     !settings.DiscordAccessibilityPrepared)
            {
                _discordRepairNeeded = true;
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
        _galleryWindow?.SetDiscordRepairNeeded(_discordRepairNeeded);
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

        SetStatus(
            _runtime.IsPaused
                ? SentoryLocalization.Text("StatusPaused")
                : SentoryLocalization.Text("StatusDetecting"),
            _runtime.IsPaused
                ? SentoryLocalization.Text("TrayPaused")
                : SentoryLocalization.Text("TrayDetecting"));
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

    private bool GetSavedDarkTheme()
    {
        try
        {
            return (_settingsStore ?? new SentorySettingsStore(_paths))
                .Load()
                .IsDarkTheme;
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
                    ? notification.Kind == ContentKind.Image
                        ? SentoryLocalization.Text("DiscordPhotoSaved")
                        : notification.Count == 1
                            ? SentoryLocalization.Text("DiscordUrlSaved")
                            : SentoryLocalization.Format(
                                "DiscordUrlsSavedFormat",
                                notification.Count)
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
                _ = _galleryWindow.RefreshAsync();
            }

            if (notification.Kind == ContentKind.Url)
            {
                WakeLinkPreviewWorker();
            }
        });
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
        if (status.SourceApp != SourceApp.Discord)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            _discordDetectionState = status.State;
            _galleryWindow?.SetDiscordDetectionState(status.State);
            if (_discordSupportEnabled)
            {
                if (status.State == CaptureRuntimeState.ReconnectRequired)
                {
                    SetDiscordRepairNeeded(true, persistPrepared: false);
                }
                else if (status.State == CaptureRuntimeState.Ready)
                {
                    SetDiscordRepairNeeded(false, persistPrepared: true);
                }
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

        var dark = _settingsStore?.Load().IsDarkTheme == true;
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
        if (_repository is null || _settingsStore is null)
        {
            return;
        }

        if (_galleryWindow is null)
        {
            _galleryWindow = new GalleryWindow(
                _repository,
                _paths,
                _settingsStore);
            _galleryWindow.DiscordRepairRequested += async (_, _) =>
                await RepairDiscordConnectionAsync();
            _galleryWindow.DiscordSupportChanged += (_, _) =>
                ApplyDiscordSupportSetting();
            _galleryWindow.LanguageChanged += (_, _) => UpdatePauseUi();
            _galleryWindow.SetDiscordRepairNeeded(
                _discordSupportEnabled && _discordRepairNeeded);
            _galleryWindow.SetDiscordDetectionState(
                _discordDetectionState);
            _galleryWindow.SetRuntimeIssue(_lastRuntimeIssue);
            _galleryWindow.Closed += (_, _) => _galleryWindow = null;
            _galleryWindow.Show();
            return;
        }

        if (_galleryWindow.WindowState == WindowState.Minimized)
        {
            _galleryWindow.WindowState = WindowState.Normal;
        }

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
        _linkPreviewFetcher?.Dispose();
        _linkPreviewFetcher = null;
        _linkPreviewService = null;
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;
        _galleryWindow?.Close();
        _galleryWindow = null;
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
        _maintenanceCancellation.Dispose();
        _linkPreviewWakeSignal.Dispose();
        base.OnExit(e);
    }
}
