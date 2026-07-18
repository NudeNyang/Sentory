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

    private readonly SentoryDataPaths _paths =
        SentoryDataPaths.ForCurrentUser();
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private Forms.NotifyIcon? _trayIcon;
    private TrayMenuWindow? _trayMenuWindow;
    private string _statusText = "시작 중...";
    private ICaptureRepository? _repository;
    private SentorySettingsStore? _settingsStore;
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
    private bool _shuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            SentoryDialogWindow.ShowMessage(
                null,
                "Sentory가 이미 실행 중입니다",
                "작업 표시줄 알림 영역의 Sentory 아이콘을 확인해 주세요.",
                GetSavedDarkTheme());
            Shutdown();
            return;
        }

        try
        {
            _settingsStore = new SentorySettingsStore(_paths);
            PrepareDiscordDefault();
            _repository = new SqliteCaptureRepository(_paths);
            await _repository.InitializeAsync();
            var repairResult = await _repository.RepairStorageAsync();
            _linkPreviewFetcher = new LinkPreviewFetcher(_paths);
            _linkPreviewService = new LinkPreviewEnrichmentService(
                _repository,
                _linkPreviewFetcher);

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

            CreateTrayIcon();
            if (repairResult.FileDeleteFailures > 0 ||
                repairResult.MissingImageFiles > 0)
            {
                _trayIcon?.ShowBalloonTip(
                    3000,
                    "Sentory 데이터 확인",
                    "일부 사진 파일을 확인하지 못했습니다. 데이터 폴더를 확인해 주세요.",
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
            SentoryDialogWindow.ShowMessage(
                null,
                "Sentory를 시작하지 못했습니다",
                exception.Message,
                GetSavedDarkTheme(),
                danger: true);
            await ShutdownRuntimeAsync();
            Shutdown(1);
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Sentory",
            Icon = SystemIcons.Application,
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
                ? "상태: 감지가 일시정지되었습니다."
                : "상태: Discord와 카카오톡을 감지하고 있습니다.",
            _runtime.IsPaused
                ? "Sentory - 감지 일시정지됨"
                : "Sentory - 메신저 감지 중");
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
                    ? "Windows 자동 실행을 켰습니다."
                    : "Windows 자동 실행을 껐습니다.",
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
                "자동 실행 설정을 변경하지 못했습니다.",
                Forms.ToolTipIcon.Warning);
        }
    }

    private void OnCaptured(
        object? sender,
        CaptureNotification notification)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _trayIcon?.ShowBalloonTip(
                2500,
                "Sentory",
                notification.SourceApp == SourceApp.Discord &&
                notification.DeliveryStatus == DeliveryStatus.Confirmed
                    ? notification.Kind == ContentKind.Image
                        ? "Discord에서 사진 전송을 확인해 저장했습니다."
                        : notification.Count == 1
                            ? "Discord에서 URL 전송을 확인해 저장했습니다."
                            : $"Discord에서 URL {notification.Count}개 전송을 확인해 저장했습니다."
                    : notification.Kind == Sentory.Core.ContentKind.Image
                    ? "사진을 입력 시 저장했습니다."
                    : notification.Count == 1
                        ? "URL을 입력 시 저장했습니다."
                        : $"URL {notification.Count}개를 입력 시 저장했습니다.",
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
            SetStatus(
                discordUnavailable
                    ? "상태: Discord 연결 복구가 필요합니다."
                    : "상태: 일부 입력 처리에 실패했지만 감지 중입니다.",
                discordUnavailable
                    ? "Sentory - Discord 연결 복구 필요"
                    : "Sentory - 메신저 감지 중");
            _trayIcon?.ShowBalloonTip(
                discordUnavailable ? 4500 : 2500,
                "Sentory",
                discordUnavailable
                    ? "Sentory 보관함에서 Discord 연결을 적용해 주세요."
                    : issue.UserMessage,
                Forms.ToolTipIcon.Warning);
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
                "Discord를 다시 연결할까요?",
                "Discord를 접근성 모드로 다시 시작합니다. 작성 중인 메시지와 진행 중인 통화가 종료될 수 있습니다.",
                "다시 시작",
                dark))
        {
            return;
        }

        _discordRepairBusy = true;
        try
        {
            await _discordLauncher.RestartAsync();
            SetDiscordRepairNeeded(false, persistPrepared: true);
            SetStatus(
                _runtime?.IsPaused == true
                    ? "상태: 감지가 일시정지되었습니다."
                    : "상태: Discord와 카카오톡을 감지하고 있습니다.",
                _runtime?.IsPaused == true
                    ? "Sentory - 감지 일시정지됨"
                    : "Sentory - 메신저 감지 중");
            _trayIcon?.ShowBalloonTip(
                3500,
                "Sentory",
                "Discord를 연결 복구 모드로 다시 시작했습니다.",
                Forms.ToolTipIcon.Info);
        }
        catch (Exception exception)
            when (exception is IOException or
                  InvalidOperationException or
                  System.ComponentModel.Win32Exception or
                  UnauthorizedAccessException)
        {
            _trayIcon?.ShowBalloonTip(
                4000,
                "Sentory",
                "Discord 연결을 복구하지 못했습니다. Discord를 종료한 뒤 다시 시도해 주세요.",
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
            _galleryWindow.SetDiscordRepairNeeded(
                _discordSupportEnabled && _discordRepairNeeded);
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
                catch (Exception)
                {
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
                    "Sentory 자동 정리",
                    $"즐겨찾기를 제외한 {result.Deleted.TotalItems:N0}개 항목을 정리했습니다.",
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
        catch (Exception)
        {
            _trayIcon?.ShowBalloonTip(
                2600,
                "Sentory 자동 정리",
                "자동 정리를 완료하지 못했습니다. 다음 실행 때 다시 시도합니다.",
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
            await _runtime.DisposeAsync();
            _runtime = null;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
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
