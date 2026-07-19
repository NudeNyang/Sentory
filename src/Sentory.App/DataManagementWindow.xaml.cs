using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.App;

public partial class DataManagementWindow : Window
{
    private static readonly CleanupOption[] AutoCleanupOptions =
    [
        new(0, "자동 정리 사용 안 함"),
        new(30, "30일 기준으로 정리"),
        new(90, "90일 기준으로 정리"),
        new(180, "180일 기준으로 정리")
    ];

    private static readonly ThemeOption[] ThemeOptions =
    [
        new(false, "라이트 모드"),
        new(true, "다크 모드")
    ];

    private readonly ICaptureRepository _repository;
    private readonly SentorySettingsStore _settingsStore;
    private readonly SentoryDataPaths _paths;
    private readonly WindowsStartupManager _startupManager = new();
    private readonly CaptureRuntimeState _discordState;
    private readonly bool _discordRepairNeeded;
    private bool _isDarkTheme;
    private bool _busy;
    private bool _initializing = true;

    public DataManagementWindow(
        ICaptureRepository repository,
        SentorySettingsStore settingsStore,
        SentoryDataPaths paths,
        CaptureRuntimeState discordState,
        bool discordRepairNeeded,
        bool isDarkTheme)
    {
        InitializeComponent();
        _repository = repository;
        _settingsStore = settingsStore;
        _paths = paths;
        _discordState = discordState;
        _discordRepairNeeded = discordRepairNeeded;
        _isDarkTheme = isDarkTheme;
        ApplyPalette();

        var settings = _settingsStore.Load();
        ThemeComboBox.ItemsSource = ThemeOptions;
        ThemeComboBox.SelectedItem = ThemeOptions.First(
            option => option.IsDark == settings.IsDarkTheme);
        AutoCleanupComboBox.ItemsSource = AutoCleanupOptions;
        AutoCleanupComboBox.SelectedItem = AutoCleanupOptions.First(
            option => option.Days == settings.AutoCleanupDays);
        VersionText.Text = $"버전 {GetVersionLabel()}";
        UpdateStartupControls();
        UpdateDiscordControls(settings.DiscordSupportEnabled);
        _initializing = false;

        Loaded += async (_, _) => await RefreshStatisticsAsync();
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
    }

    public bool HasDataChanged { get; private set; }

    public bool ThemeChanged { get; private set; }

    public bool DiscordSupportChanged { get; private set; }

    public bool DiscordRepairRequested { get; private set; }

    private async Task RefreshStatisticsAsync()
    {
        try
        {
            var statistics = await _repository.GetDataStatisticsAsync();
            TotalItemsText.Text = $"{statistics.TotalItems:N0}개";
            KindsText.Text =
                $"링크 {statistics.UrlItems:N0} · 사진 {statistics.ImageItems:N0}";
            ImageBytesText.Text = FormatBytes(statistics.ImageBytes);
            FavoriteItemsText.Text =
                $"즐겨찾기 {statistics.FavoriteItems:N0}개 보존 중";
        }
        catch (Exception)
        {
            StatusText.Text = "데이터 현황을 불러오지 못했습니다.";
        }
    }

    private void ThemeComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_initializing ||
            ThemeComboBox.SelectedItem is not ThemeOption option)
        {
            return;
        }

        try
        {
            var settings = _settingsStore.Load();
            settings.IsDarkTheme = option.IsDark;
            _settingsStore.Save(settings);
            _isDarkTheme = option.IsDark;
            ThemeChanged = true;
            ApplyPalette();
            ApplyTitleBarTheme();
            StatusText.Text = option.IsDark
                ? "다크 모드를 적용했습니다."
                : "라이트 모드를 적용했습니다.";
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "테마 설정을 저장하지 못했습니다.";
        }
    }

    private void StartupToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _startupManager.SetEnabled(!_startupManager.IsEnabled());
            UpdateStartupControls();
            StatusText.Text = _startupManager.IsEnabled()
                ? "Windows 자동 실행을 켰습니다."
                : "Windows 자동 실행을 껐습니다.";
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  System.Security.SecurityException or InvalidOperationException)
        {
            StatusText.Text = "자동 실행 설정을 변경하지 못했습니다.";
        }
    }

    private void DiscordSupportToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var settings = _settingsStore.Load();
            settings.DiscordSupportEnabled = !settings.DiscordSupportEnabled;
            _settingsStore.Save(settings);
            DiscordSupportChanged = true;
            UpdateDiscordControls(settings.DiscordSupportEnabled);
            StatusText.Text = settings.DiscordSupportEnabled
                ? "Discord 감지를 켰습니다."
                : "Discord 감지를 껐습니다.";
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Discord 감지 설정을 저장하지 못했습니다.";
        }
    }

    private void DiscordRepairButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DiscordRepairRequested = true;
        Close();
    }

    private void OpenDataFolderButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _paths.EnsureDirectories();
            Process.Start(new ProcessStartInfo
            {
                FileName = _paths.RootDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "데이터 폴더를 열지 못했습니다.";
        }
    }

    private async void DeleteNonFavoritesButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await ConfirmAndCleanupAsync(null, "즐겨찾기가 아닌 모든 항목");

    private void SaveAutoCleanupButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (AutoCleanupComboBox.SelectedItem is not CleanupOption option)
        {
            return;
        }

        try
        {
            var settings = _settingsStore.Load();
            settings.AutoCleanupDays = option.Days;
            settings.LastAutoCleanupAt = null;
            _settingsStore.Save(settings);
            StatusText.Text = option.Days == 0
                ? "자동 정리를 사용하지 않습니다."
                : $"{option.Days}일 기준 자동 정리를 저장했습니다.";
        }
        catch (Exception)
        {
            StatusText.Text = "자동 정리 설정을 저장하지 못했습니다.";
        }
    }

    private async Task ConfirmAndCleanupAsync(
        DateTimeOffset? olderThan,
        string targetDescription)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var preview = await _repository.PreviewCleanupAsync(olderThan);
            if (preview.TotalItems == 0)
            {
                StatusText.Text = "정리할 항목이 없습니다.";
                return;
            }

            var message =
                $"{targetDescription} {preview.TotalItems:N0}개를 삭제할까요?\n\n" +
                $"링크 {preview.UrlItems:N0}개 · 사진 {preview.ImageItems:N0}개 " +
                $"({FormatBytes(preview.ImageBytes)})\n" +
                "즐겨찾기는 삭제되지 않습니다.";
            if (!SentoryDialogWindow.Confirm(
                    this,
                    "항목을 정리할까요?",
                    message,
                    "모두 삭제",
                    _isDarkTheme,
                    danger: true))
            {
                StatusText.Text = "정리를 취소했습니다.";
                return;
            }

            var result = await _repository.CleanupAsync(olderThan);
            HasDataChanged = result.Deleted.TotalItems > 0;
            StatusText.Text = result.FileDeleteFailures == 0
                ? $"{result.Deleted.TotalItems:N0}개 항목을 정리했습니다."
                : $"{result.Deleted.TotalItems:N0}개를 정리했지만 일부 사진 파일은 다음 실행 때 다시 정리합니다.";
            await RefreshStatisticsAsync();
        }
        catch (Exception)
        {
            StatusText.Text = "데이터를 정리하지 못했습니다.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DeleteNonFavoritesButton.IsEnabled = !busy;
        SaveAutoCleanupButton.IsEnabled = !busy;
        AutoCleanupComboBox.IsEnabled = !busy;
        OpenDataFolderButton.IsEnabled = !busy;
        if (busy)
        {
            StatusText.Text = "삭제 대상을 확인하고 있습니다...";
        }
    }

    private void UpdateStartupControls()
    {
        try
        {
            var enabled = _startupManager.IsEnabled();
            StartupDescriptionText.Text = enabled
                ? "현재 Windows 로그인 시 자동으로 실행됩니다"
                : "현재 자동 실행을 사용하지 않습니다";
            StartupToggleButton.Content = enabled ? "끄기" : "켜기";
        }
        catch (Exception)
        {
            StartupDescriptionText.Text = "자동 실행 상태를 확인하지 못했습니다";
            StartupToggleButton.Content = "다시 시도";
        }
    }

    private void UpdateDiscordControls(bool enabled)
    {
        DiscordSupportToggleButton.Content = enabled ? "사용 중" : "사용 안 함";
        DiscordRepairButton.IsEnabled = enabled;
        DiscordStatusText.Text = !enabled
            ? "Discord 감지를 사용하지 않습니다"
            : _discordRepairNeeded
                ? "Discord 재연결 필요"
                : DiscordDetectionPresentation.GetLabel(_discordState);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:N0} {units[unit]}"
            : $"{value:N1} {units[unit]}";
    }

    private static string GetVersionLabel()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? "개발 버전"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void ApplyPalette() =>
        SentoryTheme.Apply(Resources, _isDarkTheme);

    private void ApplyTitleBarTheme() =>
        SentoryTheme.ApplyTitleBar(this, _isDarkTheme);

    private sealed record CleanupOption(int Days, string Label);

    private sealed record ThemeOption(bool IsDark, string Label);
}
