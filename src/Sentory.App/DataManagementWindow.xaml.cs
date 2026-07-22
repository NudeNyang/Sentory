using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Win32;
using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.App;

public partial class DataManagementWindow : Window
{
    private readonly ICaptureRepository _repository;
    private readonly SentorySettingsStore _settingsStore;
    private readonly SentoryDataPaths _paths;
    private readonly WindowsStartupManager _startupManager = new();
    private CaptureRuntimeState _discordState;
    private bool _discordRepairNeeded;
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
        bool discordRepairNeeded)
    {
        InitializeComponent();
        _repository = repository;
        _settingsStore = settingsStore;
        _paths = paths;
        _discordState = discordState;
        _discordRepairNeeded = discordRepairNeeded;
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
        _initializing = false;

        Loaded += async (_, _) => await RefreshStatisticsAsync();
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        OwnedPopupDismissBehavior.Enable(
            this,
            () => !_busy && !_updateCheckBusy && !_suppressBackgroundDismiss);
        SystemEvents.UserPreferenceChanged +=
            SystemEvents_UserPreferenceChanged;
        Closed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -=
                SystemEvents_UserPreferenceChanged;
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

    public event Action<SourceApp, bool>? MessengerSupportSelectionChanged;

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

    public void SetDiscordRepairNeeded(bool needed)
    {
        _discordRepairNeeded = needed;
        var settings = _settingsStore.Load();
        UpdateDiscordControls(settings.DiscordSupportEnabled);
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
        SaveAutoCleanupButton.IsEnabled = !busy;
        AutoCleanupComboBox.IsEnabled = !busy;
        OpenDataFolderButton.IsEnabled = !busy;
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
        var presentation = DiscordDetectionUiPolicy.Resolve(
            enabled,
            _discordState,
            _discordRepairNeeded);
        DiscordSupportToggleButton.Content = enabled
            ? SentoryLocalization.Text("InUse")
            : SentoryLocalization.Text("NotInUse");
        DiscordRepairButton.Visibility = presentation.ShowRepairAction
            ? Visibility.Visible
            : Visibility.Collapsed;
        DiscordStatusText.Text = !enabled
            ? SentoryLocalization.Text("DiscordNotInUse")
            : presentation.ShowRepairAction
                ? SentoryLocalization.Text("StateReconnect")
                : DiscordDetectionPresentation.GetLabel(_discordState);
    }

    private void UpdateKakaoControls(bool enabled)
    {
        KakaoSupportToggleButton.Content = enabled
            ? SentoryLocalization.Text("InUse")
            : SentoryLocalization.Text("NotInUse");
        KakaoStatusText.Text = enabled
            ? SentoryLocalization.Text("DetectionReady")
            : SentoryLocalization.Text("KakaoNotInUse");
    }

    private void UpdateMessengerControls(SentorySettings settings)
    {
        UpdateDiscordControls(settings.DiscordSupportEnabled);
        UpdateKakaoControls(settings.KakaoTalkSupportEnabled);
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

            var languageOptions = SentoryLocalization.GetLanguageOptions();
            LanguageComboBox.ItemsSource = languageOptions;
            LanguageComboBox.SelectedItem = languageOptions.First(option =>
                option.Code == settings.Language);
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

    private sealed record ThemeOption(SentoryThemeMode Mode, string Label)
    {
        public override string ToString() => Label;
    }
}
