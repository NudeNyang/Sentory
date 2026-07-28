using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;
using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Sync;
using Sentory.Platform.Windows.Runtime;
using Forms = System.Windows.Forms;

namespace Sentory.App;

public partial class DataManagementWindow : Window
{
    private readonly ICaptureRepository _repository;
    private readonly SentorySettingsStore _settingsStore;
    private readonly SentoryDataPaths _paths;
    private readonly SyncRuntimeStatusTracker _syncStatusTracker;
    private readonly WindowsStartupManager _startupManager = new();
    private readonly Task<IReadOnlyList<CloudSyncFolderCandidate>>
        _cloudSyncFolderDiscoveryTask = Task.Run(
            WindowsCloudSyncFolderDiscovery.Discover);
    private IReadOnlyList<CloudSyncFolderCandidate> _cloudSyncFolders = [];
    private bool _cloudSyncFolderDiscoveryCompleted;
    private CaptureRuntimeState _discordState;
    private bool _discordProcessRunning;
    private bool _discordRepairNeeded;
    private bool _detectionPaused;
    private SentoryThemeMode _themeMode;
    private bool _isDarkTheme;
    private bool _busy;
    private bool _updateCheckBusy;
    private bool _suppressBackgroundDismiss;
    private bool _initializing = true;
    private bool? _startupEnabled;
    private DataStatistics? _statistics;
    private int _languageChangeVersion;
    private readonly OverlayScrollIndicatorController _scrollIndicator;

    public DataManagementWindow(
        ICaptureRepository repository,
        SentorySettingsStore settingsStore,
        SentoryDataPaths paths,
        CaptureRuntimeState discordState,
        bool discordProcessRunning,
        bool discordRepairNeeded,
        bool detectionPaused,
        SyncRuntimeStatusTracker syncStatusTracker)
    {
        InitializeComponent();
        _repository = repository;
        _settingsStore = settingsStore;
        _paths = paths;
        _syncStatusTracker = syncStatusTracker;
        _discordState = discordState;
        _discordProcessRunning = discordProcessRunning;
        _discordRepairNeeded = discordRepairNeeded;
        _detectionPaused = detectionPaused;
        var settings = _settingsStore.Load();
        _themeMode = settings.GetThemeMode();
        _isDarkTheme = SentoryThemePreference.ResolveIsDark(
            _themeMode,
            SentoryThemePreference.ReadWindowsIsDark());
        ApplyPalette();
        _scrollIndicator = new OverlayScrollIndicatorController(
            SettingsScrollViewer,
            SettingsScrollSurface,
            SettingsScrollIndicator,
            SettingsScrollIndicatorThumb,
            SettingsScrollIndicatorThumbTransform);

        RefreshLocalizedOptions(settings);
        DeveloperBuildLabel.Visibility =
            SentoryBuildIdentity.IsDeveloperBuild
                ? Visibility.Visible
                : Visibility.Collapsed;
        DeveloperUpdateDivider.Visibility =
            DeveloperBuildLabel.Visibility;
        DeveloperUpdatePanel.Visibility =
            DeveloperBuildLabel.Visibility;
        VersionText.Text = SentoryLocalization.Format(
            "VersionFormat",
            GetVersionLabel());
        UpdateStartupControls();
        UpdateMessengerControls(settings);
        UpdateSyncControls(settings, _syncStatusTracker.Current);
        _initializing = false;

        Loaded += async (_, _) =>
        {
            await Task.WhenAll(
                RefreshStatisticsAsync(),
                LoadCloudSyncFoldersAsync());
        };
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        OwnedPopupDismissBehavior.Enable(
            this,
            () => !_busy && !_updateCheckBusy && !_suppressBackgroundDismiss);
        SystemEvents.UserPreferenceChanged +=
            SystemEvents_UserPreferenceChanged;
        _syncStatusTracker.Changed += SyncStatusTracker_Changed;
        Closed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -=
                SystemEvents_UserPreferenceChanged;
            _syncStatusTracker.Changed -= SyncStatusTracker_Changed;
            _scrollIndicator.Dispose();
        };
    }

    public bool ThemeChanged { get; private set; }

    public event Action<SentoryThemeMode, bool>? ThemeSelectionChanged;

    public bool LanguageChanged { get; private set; }

    public event Func<string, Task>? LanguageSelectionChanged;

    public event Func<bool, Task>? StartupSelectionChanged;

    public bool DiscordSupportChanged { get; private set; }

    public bool KakaoSupportChanged { get; private set; }

    public bool SlackSupportChanged { get; private set; }

    public bool WhatsAppSupportChanged { get; private set; }

    public bool TelegramSupportChanged { get; private set; }

    public bool LineSupportChanged { get; private set; }

    public event Action<SourceApp, bool>? MessengerSupportSelectionChanged;

    public event Action<bool, int>? AutoFavoriteSettingsChanged;

    public event Action? SyncConfigurationChanged;

    public event Func<Task>? DataChanged;

    internal event Func<Task<ManualUpdateCheckResult>>? UpdateCheckRequested;

    public bool DiscordRepairRequested { get; private set; }

    private async void UpdateCheckButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_updateCheckBusy || UpdateCheckRequested is null)
        {
            return;
        }

        _updateCheckBusy = true;
        UpdateCheckButton.IsEnabled = false;
        StatusText.Text = SentoryLocalization.Text("CheckingForUpdates");
        try
        {
            var result = await UpdateCheckRequested();
            StatusText.Text = result.Outcome switch
            {
                ManualUpdateCheckOutcome.UpToDate =>
                    SentoryLocalization.Text("AppIsUpToDate"),
                ManualUpdateCheckOutcome.UpdateAvailable =>
                    SentoryLocalization.Format(
                        "UpdateReadyFormat",
                        result.Version ?? string.Empty),
                _ => SentoryLocalization.Text("UpdateCheckFailed")
            };
        }
        catch (Exception)
        {
            StatusText.Text = SentoryLocalization.Text("UpdateCheckFailed");
        }
        finally
        {
            _updateCheckBusy = false;
            UpdateCheckButton.IsEnabled = !_busy;
        }
    }

    public void SetDiscordDetectionState(CaptureRuntimeState state)
    {
        _discordState = state;
        var settings = _settingsStore.Load();
        UpdateDiscordControls(settings.DiscordSupportEnabled);
    }

    public void SetDiscordProcessRunning(bool running)
    {
        _discordProcessRunning = running;
        var settings = _settingsStore.Load();
        UpdateDiscordControls(settings.DiscordSupportEnabled);
    }

    public void SetDiscordRepairNeeded(bool needed)
    {
        _discordRepairNeeded = needed;
        var settings = _settingsStore.Load();
        UpdateDiscordControls(settings.DiscordSupportEnabled);
    }

    public void SetDetectionPaused(bool paused)
    {
        _detectionPaused = paused;
        UpdateMessengerControls(_settingsStore.Load());
    }

    private async Task RefreshStatisticsAsync()
    {
        try
        {
            _statistics = await _repository.GetDataStatisticsAsync();
            UpdateStatisticsText(_statistics);
        }
        catch (Exception)
        {
            StatusText.Text = SentoryLocalization.Text("StatisticsLoadFailed");
        }
    }

    private async void ThemeComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_initializing ||
            ThemeComboBox.SelectedItem is not ThemeOption option)
        {
            return;
        }

        _themeMode = option.Mode;
        _isDarkTheme = SentoryThemePreference.ResolveIsDark(
            _themeMode,
            SentoryThemePreference.ReadWindowsIsDark());
        ThemeChanged = true;
        ApplyPalette();
        ApplyTitleBarTheme();
        ThemeSelectionChanged?.Invoke(_themeMode, _isDarkTheme);
        StatusText.Text = SentoryLocalization.Text(
            SentoryThemePreference.AppliedMessageKey(
                _themeMode,
                _isDarkTheme));

        await Dispatcher.Yield(DispatcherPriority.Background);

        try
        {
            var settings = _settingsStore.Load();
            settings.ThemeMode = _themeMode.ToString();
            settings.IsDarkTheme = _isDarkTheme;
            _settingsStore.Save(settings);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = SentoryLocalization.Text("ThemeSaveFailed");
        }
    }

    private void SystemEvents_UserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        if (_themeMode != SentoryThemeMode.System)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var isDark = SentoryThemePreference.ReadWindowsIsDark();
            if (_isDarkTheme == isDark)
            {
                return;
            }

            _isDarkTheme = isDark;
            ApplyPalette();
            ApplyTitleBarTheme();
            ThemeSelectionChanged?.Invoke(_themeMode, _isDarkTheme);
        });
    }

    private async void LanguageComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_initializing ||
            LanguageComboBox.SelectedItem is not
                SentoryLocalization.LanguageOption option)
        {
            return;
        }

        var languageChangeVersion = ++_languageChangeVersion;
        try
        {
            var settings = _settingsStore.Load();
            settings.Language = option.Code;
            SentoryLocalization.SetLanguage(settings.Language);
            SentoryLocalization.ApplyCurrent(Resources);
            LanguageChanged = true;
            RefreshLocalizedOptions(settings);
            VersionText.Text = SentoryLocalization.Format(
                "VersionFormat",
                GetVersionLabel());
            if (_startupEnabled is { } startupEnabled)
            {
                UpdateStartupControls(startupEnabled);
            }
            UpdateMessengerControls(settings);
            if (_statistics is not null)
            {
                UpdateStatisticsText(_statistics);
            }
            StatusText.Text = SentoryLocalization.Text("LanguageApplied");
            await Dispatcher.Yield(DispatcherPriority.Render);
            if (languageChangeVersion != _languageChangeVersion)
            {
                return;
            }

            await NotifyLanguageSelectionChangedAsync(settings.Language);
            if (languageChangeVersion != _languageChangeVersion)
            {
                return;
            }

            await Dispatcher.InvokeAsync(
                () => SentoryLocalization.ApplyCurrent(
                    System.Windows.Application.Current.Resources),
                DispatcherPriority.Background);
            await Task.Run(() => _settingsStore.Save(settings));
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = SentoryLocalization.Text("LanguageSaveFailed");
        }
    }

    private async Task NotifyLanguageSelectionChangedAsync(string language)
    {
        if (LanguageSelectionChanged is null)
        {
            return;
        }

        foreach (Func<string, Task> handler in
                 LanguageSelectionChanged.GetInvocationList())
        {
            await handler(language);
        }
    }

    private async void StartupToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!StartupToggleButton.IsEnabled)
        {
            return;
        }

        bool enabled;
        try
        {
            enabled = !_startupManager.IsEnabled();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  System.Security.SecurityException)
        {
            StatusText.Text = SentoryLocalization.Text("StartupChangeFailed");
            return;
        }

        StartupToggleButton.IsEnabled = false;
        UpdateStartupControls(enabled);
        StatusText.Text = enabled
            ? SentoryLocalization.Text("StartupEnabled")
            : SentoryLocalization.Text("StartupDisabled");
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            await Task.Run(() => _startupManager.SetEnabled(enabled));
            var settings = _settingsStore.Load();
            settings.StartWithWindows = enabled;
            _settingsStore.Save(settings);
            await NotifyStartupSelectionChangedAsync(enabled);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  System.Security.SecurityException or InvalidOperationException)
        {
            UpdateStartupControls();
            StatusText.Text = SentoryLocalization.Text("StartupChangeFailed");
        }
        finally
        {
            StartupToggleButton.IsEnabled = true;
        }
    }

    private async Task NotifyStartupSelectionChangedAsync(bool enabled)
    {
        if (StartupSelectionChanged is null)
        {
            return;
        }

        foreach (Func<bool, Task> handler in
                 StartupSelectionChanged.GetInvocationList())
        {
            await handler(enabled);
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
            MessengerSupportSelectionChanged?.Invoke(
                SourceApp.Discord,
                settings.DiscordSupportEnabled);
            StatusText.Text = settings.DiscordSupportEnabled
                ? SentoryLocalization.Text("DiscordDetectionEnabled")
                : SentoryLocalization.Text("DiscordDetectionDisabled");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = SentoryLocalization.Text("DiscordSettingFailed");
        }
    }

    private void KakaoSupportToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var settings = _settingsStore.Load();
            settings.KakaoTalkSupportEnabled =
                !settings.KakaoTalkSupportEnabled;
            _settingsStore.Save(settings);
            KakaoSupportChanged = true;
            UpdateKakaoControls(settings.KakaoTalkSupportEnabled);
            MessengerSupportSelectionChanged?.Invoke(
                SourceApp.KakaoTalk,
                settings.KakaoTalkSupportEnabled);
            StatusText.Text = settings.KakaoTalkSupportEnabled
                ? SentoryLocalization.Text("KakaoDetectionEnabled")
                : SentoryLocalization.Text("KakaoDetectionDisabled");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = SentoryLocalization.Text("KakaoSettingFailed");
        }
    }

    private void DiscordRepairButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DiscordRepairRequested = true;
        Close();
    }

    private async void ChooseSyncFolderButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await ChooseAndEnableSyncFolderAsync();

    private async void SyncToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var settings = _settingsStore.Load();
        if (!settings.SyncEnabled &&
            string.IsNullOrWhiteSpace(settings.SyncFolderPath))
        {
            await StartSyncWithAutomaticFolderAsync();
            return;
        }

        try
        {
            settings.SyncEnabled = !settings.SyncEnabled;
            if (settings.SyncEnabled &&
                !SyncDeviceIdentity.IsValid(settings.SyncDeviceId))
            {
                settings.SyncDeviceId = SyncDeviceIdentity.Create();
            }

            _settingsStore.Save(settings);
            UpdateSyncControls(settings, _syncStatusTracker.Current);
            SyncConfigurationChanged?.Invoke();
            StatusText.Text = SentoryLocalization.Text(
                settings.SyncEnabled
                    ? "SyncEnabledSaved"
                    : "SyncDisabledSaved");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text =
                SentoryLocalization.Text("SyncSettingFailed");
        }
    }

    private void SlackSupportToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var settings = _settingsStore.Load();
            settings.SlackSupportEnabled = !settings.SlackSupportEnabled;
            _settingsStore.Save(settings);
            SlackSupportChanged = true;
            UpdateSlackControls(settings.SlackSupportEnabled);
            MessengerSupportSelectionChanged?.Invoke(
                SourceApp.Slack,
                settings.SlackSupportEnabled);
            StatusText.Text = settings.SlackSupportEnabled
                ? SentoryLocalization.Text("SlackDetectionEnabled")
                : SentoryLocalization.Text("SlackDetectionDisabled");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = SentoryLocalization.Text("SlackSettingFailed");
        }
    }

    private void WhatsAppSupportToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var settings = _settingsStore.Load();
            settings.WhatsAppSupportEnabled =
                !settings.WhatsAppSupportEnabled;
            _settingsStore.Save(settings);
            WhatsAppSupportChanged = true;
            UpdateWhatsAppControls(settings.WhatsAppSupportEnabled);
            MessengerSupportSelectionChanged?.Invoke(
                SourceApp.WhatsApp,
                settings.WhatsAppSupportEnabled);
            StatusText.Text = settings.WhatsAppSupportEnabled
                ? SentoryLocalization.Text("WhatsAppDetectionEnabled")
                : SentoryLocalization.Text("WhatsAppDetectionDisabled");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text =
                SentoryLocalization.Text("WhatsAppSettingFailed");
        }
    }

    private void LineSupportToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var settings = _settingsStore.Load();
            settings.LineSupportEnabled = !settings.LineSupportEnabled;
            _settingsStore.Save(settings);
            LineSupportChanged = true;
            UpdateLineControls(settings.LineSupportEnabled);
            MessengerSupportSelectionChanged?.Invoke(
                SourceApp.Line,
                settings.LineSupportEnabled);
            StatusText.Text = settings.LineSupportEnabled
                ? SentoryLocalization.Text("LineDetectionEnabled")
                : SentoryLocalization.Text("LineDetectionDisabled");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = SentoryLocalization.Text("LineSettingFailed");
        }
    }

    private void TelegramSupportToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var settings = _settingsStore.Load();
            settings.TelegramSupportEnabled =
                !settings.TelegramSupportEnabled;
            _settingsStore.Save(settings);
            TelegramSupportChanged = true;
            UpdateTelegramControls(settings.TelegramSupportEnabled);
            MessengerSupportSelectionChanged?.Invoke(
                SourceApp.Telegram,
                settings.TelegramSupportEnabled);
            StatusText.Text = settings.TelegramSupportEnabled
                ? SentoryLocalization.Text("TelegramDetectionEnabled")
                : SentoryLocalization.Text("TelegramDetectionDisabled");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text =
                SentoryLocalization.Text("TelegramSettingFailed");
        }
    }

    private async Task StartSyncWithAutomaticFolderAsync()
    {
        await LoadCloudSyncFoldersAsync();
        var candidate = _cloudSyncFolders.Count switch
        {
            0 => null,
            1 => _cloudSyncFolders[0],
            _ => SyncProviderComboBox.SelectedItem as
                CloudSyncFolderCandidate
        };
        if (candidate is null)
        {
            await ChooseAndEnableSyncFolderAsync();
            return;
        }

        await EnableSyncFolderAsync(
            candidate.FolderPath,
            candidate.ProviderName);
    }

    private async Task LoadCloudSyncFoldersAsync()
    {
        if (_cloudSyncFolderDiscoveryCompleted)
        {
            return;
        }

        try
        {
            _cloudSyncFolders = await _cloudSyncFolderDiscoveryTask;
        }
        catch (Exception)
        {
            _cloudSyncFolders = [];
        }

        _cloudSyncFolderDiscoveryCompleted = true;
        SyncProviderComboBox.ItemsSource = _cloudSyncFolders;
        if (_cloudSyncFolders.Count > 0)
        {
            SyncProviderComboBox.SelectedIndex = 0;
        }

        UpdateSyncControls(
            _settingsStore.Load(),
            _syncStatusTracker.Current);
    }

    private async Task ChooseAndEnableSyncFolderAsync()
    {
        if (_busy)
        {
            return;
        }

        var settings = _settingsStore.Load();
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description =
                SentoryLocalization.Text("ChooseSyncFolderDescription"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = settings.SyncFolderPath ?? string.Empty
        };
        _suppressBackgroundDismiss = true;
        try
        {
            if (dialog.ShowDialog() != Forms.DialogResult.OK ||
                string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return;
            }

            await EnableSyncFolderAsync(dialog.SelectedPath);
        }
        finally
        {
            _suppressBackgroundDismiss = false;
        }
    }

    private async Task EnableSyncFolderAsync(
        string folderPath,
        string? automaticProviderName = null)
    {
        var settings = _settingsStore.Load();
        try
        {
            var selectedPath = Path.GetFullPath(folderPath);
            Directory.CreateDirectory(selectedPath);
            var oldFolderPath = settings.SyncFolderPath;
            var oldDeviceId = settings.SyncDeviceId;
            var oldStorageVersion = settings.SyncStorageVersion;
            var oldMigrationDeviceId = settings.SyncMigrationDeviceId;
            var oldStoreId = settings.SyncStoreId;
            var capability = await SyncFolderCapabilityProbe.CheckAsync(
                selectedPath);
            if (!capability.IsSupported)
            {
                StatusText.Text = SentoryLocalization.Text(
                    capability.FailureReason switch
                    {
                        SyncFolderCapabilityFailure.NotDirectory =>
                            "SyncFolderNotDirectory",
                        SyncFolderCapabilityFailure.RenameUnavailable =>
                            "SyncFolderRenameUnavailable",
                        SyncFolderCapabilityFailure.ContentMismatch =>
                            "SyncFolderContentMismatch",
                        _ => "SyncFolderReadWriteUnavailable"
                    });
                return;
            }
            var folderChanged =
                !string.IsNullOrWhiteSpace(oldFolderPath) &&
                !string.Equals(
                    Path.GetFullPath(oldFolderPath),
                    selectedPath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal);
            settings.SyncFolderPath = selectedPath;
            settings.SyncEnabled = true;
            var isNewStore = folderChanged ||
                             string.IsNullOrWhiteSpace(oldFolderPath);
            settings.SyncStorageVersion = isNewStore
                ? SentorySettings.CurrentSyncStorageVersion
                : oldStorageVersion;
            settings.SyncMigrationDeviceId = isNewStore
                ? null
                : oldMigrationDeviceId;
            settings.SyncStoreId = isNewStore
                ? null
                : oldStoreId;
            if (folderChanged ||
                !SyncDeviceIdentity.IsValid(settings.SyncDeviceId))
            {
                settings.SyncDeviceId = SyncDeviceIdentity.Create();
            }

            _settingsStore.Save(settings);
            if (folderChanged)
            {
                try
                {
                    await SqliteSyncOperationJournal
                        .ResetForNewStoreAsync(
                            _paths,
                            settings.SyncDeviceId!);
                }
                catch
                {
                    settings.SyncFolderPath = oldFolderPath;
                    settings.SyncDeviceId = oldDeviceId;
                    settings.SyncStorageVersion = oldStorageVersion;
                    settings.SyncMigrationDeviceId = oldMigrationDeviceId;
                    settings.SyncStoreId = oldStoreId;
                    settings.SyncEnabled = false;
                    _settingsStore.Save(settings);
                    throw;
                }
            }

            UpdateSyncControls(settings, _syncStatusTracker.Current);
            SyncConfigurationChanged?.Invoke();
            StatusText.Text = automaticProviderName is null
                ? SentoryLocalization.Text("SyncFolderSaved")
                : SentoryLocalization.Format(
                    "SyncAutomaticFolderSavedFormat",
                    automaticProviderName);
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  ArgumentException or
                  NotSupportedException or
                  InvalidOperationException or
                  System.Data.Common.DbException)
        {
            StatusText.Text =
                SentoryLocalization.Text("SyncSettingFailed");
        }
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
            StatusText.Text = SentoryLocalization.Text("OpenDataFolderFailed");
        }
    }

    private void ViewLicenseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var window = new LicenseWindow(_isDarkTheme)
        {
            Owner = this
        };
        _suppressBackgroundDismiss = true;
        try
        {
            window.ShowDialog();
        }
        finally
        {
            _suppressBackgroundDismiss = false;
        }
    }

    private void GitHubLink_RequestNavigate(
        object sender,
        RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or
                  System.ComponentModel.Win32Exception)
        {
            StatusText.Text = SentoryLocalization.Text("OpenGitHubFailed");
        }
    }

    private async void DeleteNonFavoritesButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await ConfirmAndCleanupAsync(
            null,
            SentoryLocalization.Text("AllNonFavoriteItems"));

    private void SaveAutoFavoriteButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (AutoFavoriteComboBox.SelectedItem is not
            AutoFavoriteOption option)
        {
            return;
        }

        try
        {
            var settings = _settingsStore.Load();
            if (settings.AutoFavoriteEnabled != option.Enabled ||
                settings.AutoFavoriteCopyThreshold != option.CopyThreshold)
            {
                settings.AutoFavoriteEnabled = option.Enabled;
                settings.AutoFavoriteCopyThreshold = option.CopyThreshold;
                settings.AutoFavoriteChangedAt = DateTimeOffset.UtcNow;
            }
            _settingsStore.Save(settings);
            AutoFavoriteSettingsChanged?.Invoke(
                option.Enabled,
                option.CopyThreshold);
            StatusText.Text = option.Enabled
                ? SentoryLocalization.Format(
                    "AutoFavoriteSavedFormat",
                    option.CopyThreshold)
                : SentoryLocalization.Text("AutoFavoriteDisabled");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text =
                SentoryLocalization.Text("AutoFavoriteSaveFailed");
        }
    }

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
                ? SentoryLocalization.Text("AutoCleanupDisabled")
                : SentoryLocalization.Format(
                    "AutoCleanupSavedFormat",
                    option.Days);
        }
        catch (Exception)
        {
            StatusText.Text = SentoryLocalization.Text("AutoCleanupSaveFailed");
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
                StatusText.Text = SentoryLocalization.Text("NothingToCleanup");
                return;
            }

            var message = SentoryLocalization.Format(
                "CleanupConfirmMessage",
                targetDescription,
                preview.TotalItems,
                preview.UrlItems,
                preview.ImageItems,
                FormatBytes(preview.ImageBytes));
            if (!SentoryDialogWindow.Confirm(
                    this,
                    SentoryLocalization.Text("CleanupConfirmHeading"),
                    message,
                    SentoryLocalization.Text("DeleteAll"),
                    _isDarkTheme,
                    danger: true))
            {
                StatusText.Text = SentoryLocalization.Text("CleanupCancelled");
                return;
            }

            var result = await _repository.CleanupAsync(olderThan);
            if (result.Deleted.TotalItems > 0)
            {
                await NotifyDataChangedAsync();
            }
            StatusText.Text = result.FileDeleteFailures == 0
                ? SentoryLocalization.Format(
                    "CleanupCompleteFormat",
                    result.Deleted.TotalItems)
                : SentoryLocalization.Format(
                    "CleanupPartialFormat",
                    result.Deleted.TotalItems);
            await RefreshStatisticsAsync();
        }
        catch (Exception)
        {
            StatusText.Text = SentoryLocalization.Text("CleanupFailed");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task NotifyDataChangedAsync()
    {
        if (DataChanged is null)
        {
            return;
        }

        foreach (var handler in DataChanged
                     .GetInvocationList()
                     .Cast<Func<Task>>())
        {
            await handler();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DeleteNonFavoritesButton.IsEnabled = !busy;
        SaveAutoFavoriteButton.IsEnabled = !busy;
        AutoFavoriteComboBox.IsEnabled = !busy;
        SaveAutoCleanupButton.IsEnabled = !busy;
        AutoCleanupComboBox.IsEnabled = !busy;
        OpenDataFolderButton.IsEnabled = !busy;
        ChooseSyncFolderButton.IsEnabled = !busy;
        ManualSyncFolderButton.IsEnabled = !busy;
        SyncProviderComboBox.IsEnabled = !busy;
        SyncToggleButton.IsEnabled = !busy;
        UpdateCheckButton.IsEnabled = !busy && !_updateCheckBusy;
        if (busy)
        {
            StatusText.Text = SentoryLocalization.Text("CheckingCleanup");
        }
    }

    private void UpdateStartupControls()
    {
        try
        {
            _startupEnabled = _startupManager.IsEnabled();
            UpdateStartupControls(_startupEnabled.Value);
        }
        catch (Exception)
        {
            _startupEnabled = null;
            StartupDescriptionText.Text =
                SentoryLocalization.Text("StartupStatusFailed");
            StartupToggleButton.Content = SentoryLocalization.Text("Retry");
        }
    }

    private void UpdateStartupControls(bool enabled)
    {
        _startupEnabled = enabled;
        StartupDescriptionText.Text = enabled
            ? SentoryLocalization.Text("StartupCurrentlyEnabled")
            : SentoryLocalization.Text("StartupCurrentlyDisabled");
        StartupToggleButton.Content = enabled
            ? SentoryLocalization.Text("TurnOff")
            : SentoryLocalization.Text("TurnOn");
    }

    private void UpdateStatisticsText(DataStatistics statistics)
    {
        TotalItemsText.Text = SentoryLocalization.Format(
            "ItemsCountFormat",
            statistics.TotalItems);
        KindsText.Text = SentoryLocalization.Format(
            "KindsCountFormat",
            statistics.UrlItems,
            statistics.ImageItems);
        ImageBytesText.Text = FormatBytes(statistics.ImageBytes);
        FavoriteItemsText.Text = SentoryLocalization.Format(
            "FavoritesPreservedFormat",
            statistics.FavoriteItems);
    }

    private void UpdateDiscordControls(bool enabled)
    {
        var settingsState = MessengerDetectionSettingsUiPolicy.Resolve(
            enabled,
            _detectionPaused);
        var presentation = DiscordDetectionUiPolicy.Resolve(
            enabled,
            _discordProcessRunning,
            _discordState,
            _discordRepairNeeded);
        DiscordSupportToggleButton.Content = enabled
            ? SentoryLocalization.Text("InUse")
            : SentoryLocalization.Text("NotInUse");
        DiscordRepairButton.Visibility =
            settingsState == MessengerDetectionSettingsState.Active &&
            presentation.ShowRepairAction
            ? Visibility.Visible
            : Visibility.Collapsed;
        DiscordStatusText.Text = settingsState switch
        {
            MessengerDetectionSettingsState.Disabled =>
                SentoryLocalization.Text("DiscordNotInUse"),
            MessengerDetectionSettingsState.Paused =>
                SentoryLocalization.Text("DetectionPaused"),
            _ when !_discordProcessRunning =>
                SentoryLocalization.Text("DiscordNotRunning"),
            _ when presentation.ShowRepairAction =>
                SentoryLocalization.Text("StateReconnect"),
            _ => DiscordDetectionPresentation.GetLabel(_discordState)
        };
    }

    private void UpdateKakaoControls(bool enabled)
    {
        var settingsState = MessengerDetectionSettingsUiPolicy.Resolve(
            enabled,
            _detectionPaused);
        KakaoSupportToggleButton.Content = enabled
            ? SentoryLocalization.Text("InUse")
            : SentoryLocalization.Text("NotInUse");
        KakaoStatusText.Text = settingsState switch
        {
            MessengerDetectionSettingsState.Disabled =>
                SentoryLocalization.Text("KakaoNotInUse"),
            MessengerDetectionSettingsState.Paused =>
                SentoryLocalization.Text("DetectionPaused"),
            _ => SentoryLocalization.Text("DetectionReady")
        };
    }

    private void UpdateSlackControls(bool enabled)
    {
        var settingsState = MessengerDetectionSettingsUiPolicy.Resolve(
            enabled,
            _detectionPaused);
        SlackSupportToggleButton.Content = enabled
            ? SentoryLocalization.Text("InUse")
            : SentoryLocalization.Text("NotInUse");
        SlackStatusText.Text = settingsState switch
        {
            MessengerDetectionSettingsState.Disabled =>
                SentoryLocalization.Text("SlackNotInUse"),
            MessengerDetectionSettingsState.Paused =>
                SentoryLocalization.Text("DetectionPaused"),
            _ => SentoryLocalization.Text("DetectionReady")
        };
    }

    private void UpdateWhatsAppControls(bool enabled)
    {
        var settingsState = MessengerDetectionSettingsUiPolicy.Resolve(
            enabled,
            _detectionPaused);
        WhatsAppSupportToggleButton.Content = enabled
            ? SentoryLocalization.Text("InUse")
            : SentoryLocalization.Text("NotInUse");
        WhatsAppStatusText.Text = settingsState switch
        {
            MessengerDetectionSettingsState.Disabled =>
                SentoryLocalization.Text("WhatsAppNotInUse"),
            MessengerDetectionSettingsState.Paused =>
                SentoryLocalization.Text("DetectionPaused"),
            _ => SentoryLocalization.Text("DetectionReady")
        };
    }

    private void UpdateLineControls(bool enabled)
    {
        var settingsState = MessengerDetectionSettingsUiPolicy.Resolve(
            enabled,
            _detectionPaused);
        LineSupportToggleButton.Content = enabled
            ? SentoryLocalization.Text("InUse")
            : SentoryLocalization.Text("NotInUse");
        LineStatusText.Text = settingsState switch
        {
            MessengerDetectionSettingsState.Disabled =>
                SentoryLocalization.Text("LineNotInUse"),
            MessengerDetectionSettingsState.Paused =>
                SentoryLocalization.Text("DetectionPaused"),
            _ => SentoryLocalization.Text("DetectionReady")
        };
    }

    private void UpdateTelegramControls(bool enabled)
    {
        var settingsState = MessengerDetectionSettingsUiPolicy.Resolve(
            enabled,
            _detectionPaused);
        TelegramSupportToggleButton.Content = enabled
            ? SentoryLocalization.Text("InUse")
            : SentoryLocalization.Text("NotInUse");
        TelegramStatusText.Text = settingsState switch
        {
            MessengerDetectionSettingsState.Disabled =>
                SentoryLocalization.Text("TelegramNotInUse"),
            MessengerDetectionSettingsState.Paused =>
                SentoryLocalization.Text("DetectionPaused"),
            _ => SentoryLocalization.Text("DetectionReady")
        };
    }

    private void UpdateMessengerControls(SentorySettings settings)
    {
        UpdateDiscordControls(settings.DiscordSupportEnabled);
        UpdateKakaoControls(settings.KakaoTalkSupportEnabled);
        UpdateSlackControls(settings.SlackSupportEnabled);
        UpdateWhatsAppControls(settings.WhatsAppSupportEnabled);
        UpdateTelegramControls(settings.TelegramSupportEnabled);
        UpdateLineControls(settings.LineSupportEnabled);
    }

    private void SyncStatusTracker_Changed(
        SyncRuntimeSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                () => SyncStatusTracker_Changed(snapshot));
            return;
        }

        UpdateSyncControls(_settingsStore.Load(), snapshot);
    }

    private void UpdateSyncControls(
        SentorySettings settings,
        SyncRuntimeSnapshot snapshot)
    {
        var hasFolder = !string.IsNullOrWhiteSpace(
            settings.SyncFolderPath);
        SyncFolderPathText.Text = hasFolder
            ? settings.SyncFolderPath
            : string.Empty;
        SyncFolderPathText.Visibility = hasFolder
            ? Visibility.Visible
            : Visibility.Collapsed;
        SyncSetupDescriptionText.Visibility = hasFolder
            ? Visibility.Collapsed
            : Visibility.Visible;
        SyncProviderComboBox.Visibility = !hasFolder &&
                                          _cloudSyncFolderDiscoveryCompleted &&
                                          _cloudSyncFolders.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        SyncSetupDescriptionText.Text = !_cloudSyncFolderDiscoveryCompleted
            ? SentoryLocalization.Text("SyncLocationDetecting")
            : _cloudSyncFolders.Count switch
            {
                0 => SentoryLocalization.Text(
                    "SyncAutomaticLocationUnavailable"),
                1 => SentoryLocalization.Format(
                    "SyncAutomaticLocationFormat",
                    _cloudSyncFolders[0].ProviderName),
                _ => SentoryLocalization.Text(
                    "SyncProviderSelectionDescription")
            };
        ChooseSyncFolderButton.Visibility = hasFolder
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChooseSyncFolderButton.Content = SentoryLocalization.Text(
            "ChangeSyncFolder");
        ManualSyncFolderButton.Visibility = hasFolder
            ? Visibility.Collapsed
            : Visibility.Visible;
        SyncToggleButton.Content = SentoryLocalization.Text(
            hasFolder
                ? settings.SyncEnabled ? "TurnOff" : "TurnOn"
                : "StartSync");
        Grid.SetColumn(SyncToggleButton, hasFolder ? 2 : 0);
        Grid.SetColumnSpan(SyncToggleButton, hasFolder ? 1 : 3);
        SyncToggleButton.Width = hasFolder ? 82 : double.NaN;
        SyncToggleButton.Visibility = Visibility.Visible;
        SyncRuntimeStatusText.Text = settings.SyncEnabled
            ? SentoryLocalization.Text(snapshot.State switch
            {
                SyncRuntimeState.Syncing => "SyncStateSyncing",
                SyncRuntimeState.Migrating => "SyncStateMigrating",
                SyncRuntimeState.Recovering => "SyncStateRecovering",
                SyncRuntimeState.Succeeded => "SyncStateSucceeded",
                SyncRuntimeState.FolderUnavailable =>
                    "SyncStateFolderUnavailable",
                SyncRuntimeState.InvalidData => "SyncStateInvalidData",
                SyncRuntimeState.Failed => "SyncStateFailed",
                _ => "SyncStateWaiting"
            })
            : SentoryLocalization.Text("SyncStateDisabled");
    }

    private void RefreshLocalizedOptions(SentorySettings settings)
    {
        _initializing = true;
        try
        {
            var themeOptions = new[]
            {
                new ThemeOption(
                    SentoryThemeMode.Light,
                    SentoryLocalization.Text("LightMode")),
                new ThemeOption(
                    SentoryThemeMode.Dark,
                    SentoryLocalization.Text("DarkMode")),
                new ThemeOption(
                    SentoryThemeMode.System,
                    SentoryLocalization.Text("SystemTheme"))
            };
            ThemeComboBox.ItemsSource = themeOptions;
            ThemeComboBox.SelectedItem = themeOptions.First(option =>
                option.Mode == settings.GetThemeMode());

            var cleanupOptions = new[]
            {
                new CleanupOption(0, SentoryLocalization.Text("CleanupOff")),
                new CleanupOption(7, SentoryLocalization.Text("Cleanup7")),
                new CleanupOption(30, SentoryLocalization.Text("Cleanup30")),
                new CleanupOption(90, SentoryLocalization.Text("Cleanup90")),
                new CleanupOption(180, SentoryLocalization.Text("Cleanup180"))
            };
            AutoCleanupComboBox.ItemsSource = cleanupOptions;
            AutoCleanupComboBox.SelectedItem = cleanupOptions.First(option =>
                option.Days == settings.AutoCleanupDays);

            var autoFavoriteOptions = new List<AutoFavoriteOption>
            {
                new(
                    false,
                    settings.AutoFavoriteCopyThreshold,
                    SentoryLocalization.Text("AutoFavoriteOff"))
            };
            autoFavoriteOptions.AddRange(Enumerable
                .Range(
                    SentorySettings.MinimumAutoFavoriteCopyThreshold,
                    SentorySettings.MaximumAutoFavoriteCopyThreshold -
                    SentorySettings.MinimumAutoFavoriteCopyThreshold + 1)
                .Select(copyThreshold => new AutoFavoriteOption(
                    true,
                    copyThreshold,
                    SentoryLocalization.Format(
                        "AutoFavoriteCopyCountFormat",
                        copyThreshold))));
            AutoFavoriteComboBox.ItemsSource = autoFavoriteOptions;
            AutoFavoriteComboBox.SelectedItem =
                settings.AutoFavoriteEnabled
                    ? autoFavoriteOptions.First(option =>
                        option.Enabled &&
                        option.CopyThreshold ==
                        settings.AutoFavoriteCopyThreshold)
                    : autoFavoriteOptions[0];

            var languageOptions = SentoryLocalization.GetLanguageOptions();
            LanguageComboBox.ItemsSource = languageOptions;
            LanguageComboBox.SelectedItem = languageOptions.First(option =>
                option.Code == settings.Language);
            UpdateSyncControls(settings, _syncStatusTracker.Current);
        }
        finally
        {
            _initializing = false;
        }
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
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparator = informationalVersion.IndexOf('+');
            return metadataSeparator >= 0
                ? informationalVersion[..metadataSeparator]
                : informationalVersion;
        }

        var version = assembly.GetName().Version;
        return version is null
            ? SentoryLocalization.Text("DevelopmentVersion")
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void ApplyPalette() =>
        SentoryTheme.Apply(Resources, _isDarkTheme);

    private void ApplyTitleBarTheme() =>
        SentoryTheme.ApplyTitleBar(this, _isDarkTheme);

    private sealed record CleanupOption(int Days, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record AutoFavoriteOption(
        bool Enabled,
        int CopyThreshold,
        string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ThemeOption(SentoryThemeMode Mode, string Label)
    {
        public override string ToString() => Label;
    }
}
