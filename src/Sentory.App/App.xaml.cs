using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using Sentory.Core;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Links;
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
    private Forms.ToolStripMenuItem? _statusItem;
    private Forms.ToolStripMenuItem? _pauseItem;
    private Forms.ToolStripMenuItem? _startupItem;
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
    private bool _shuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Sentory가 이미 실행 중입니다. 작업 표시줄 알림 영역을 확인해 주세요.",
                "Sentory",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _settingsStore = new SentorySettingsStore(_paths);
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
            _runtime = new KakaoCaptureRuntime(
                _repository,
                acceptInjectedInput);
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
            System.Windows.MessageBox.Show(
                $"Sentory를 시작하지 못했습니다.\n\n{exception.Message}",
                "Sentory 시작 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await ShutdownRuntimeAsync();
            Shutdown(1);
        }
    }

    private void CreateTrayIcon()
    {
        _statusItem = new Forms.ToolStripMenuItem("시작 중...")
        {
            Enabled = false
        };
        _pauseItem = new Forms.ToolStripMenuItem("감지 일시정지")
        {
            CheckOnClick = true
        };
        _pauseItem.Click += (_, _) => ApplyPauseState();

        _startupItem = new Forms.ToolStripMenuItem(
            "Windows 시작 시 자동 실행")
        {
            CheckOnClick = true,
            Checked = GetStartupEnabled()
        };
        _startupItem.Click += (_, _) => ApplyStartupState();

        var openGalleryItem = new Forms.ToolStripMenuItem("갤러리 열기");
        openGalleryItem.Click += (_, _) => OpenGallery();

        var openDataItem = new Forms.ToolStripMenuItem("데이터 폴더 열기");
        openDataItem.Click += (_, _) => OpenDataFolder();

        var exitItem = new Forms.ToolStripMenuItem("종료");
        exitItem.Click += async (_, _) =>
        {
            await ShutdownRuntimeAsync();
            Shutdown();
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(openGalleryItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(openDataItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Sentory",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => OpenGallery();
    }

    private void ApplyPauseState()
    {
        if (_runtime is null || _pauseItem is null)
        {
            return;
        }

        _runtime.IsPaused = _pauseItem.Checked;
        UpdatePauseUi();
    }

    private void UpdatePauseUi()
    {
        if (_runtime is null || _pauseItem is null)
        {
            return;
        }

        _pauseItem.Checked = _runtime.IsPaused;
        SetStatus(
            _runtime.IsPaused
                ? "상태: 감지가 일시정지되었습니다."
                : "상태: 카카오톡을 감지하고 있습니다.",
            _runtime.IsPaused
                ? "Sentory - 감지 일시정지됨"
                : "Sentory - 카카오톡 감지 중");
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

    private void ApplyStartupState()
    {
        if (_startupItem is null)
        {
            return;
        }

        try
        {
            _startupManager.SetEnabled(_startupItem.Checked);
            _trayIcon?.ShowBalloonTip(
                1800,
                "Sentory",
                _startupItem.Checked
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
            _startupItem.Checked = !_startupItem.Checked;
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
                notification.Kind == Sentory.Core.ContentKind.Image
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
            SetStatus(
                "상태: 일부 입력 처리에 실패했지만 감지 중입니다.",
                "Sentory - 카카오톡 감지 중");
            _trayIcon?.ShowBalloonTip(
                2500,
                "Sentory",
                issue.UserMessage,
                Forms.ToolTipIcon.Warning);
        });
    }

    private void SetStatus(string status, string trayText)
    {
        if (_statusItem is not null)
        {
            _statusItem.Text = status;
        }

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
