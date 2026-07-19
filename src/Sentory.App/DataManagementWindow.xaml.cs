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
        RefreshLocalizedOptions(settings);
        VersionText.Text = SentoryLocalization.Format(
            "VersionFormat",
            GetVersionLabel());
        UpdateStartupControls();
        UpdateDiscordControls(settings.DiscordSupportEnabled);
        _initializing = false;

        Loaded += async (_, _) => await RefreshStatisticsAsync();
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
    }

    public bool HasDataChanged { get; private set; }

    public bool ThemeChanged { get; private set; }

    public bool LanguageChanged { get; private set; }

    public bool DiscordSupportChanged { get; private set; }

    public bool DiscordRepairRequested { get; private set; }

    private async Task RefreshStatisticsAsync()
    {
        try
        {
            var statistics = await _repository.GetDataStatisticsAsync();
            TotalItemsText.Text = SentoryLocalization.Format(
                "ItemsCountFormat",
                statistics.TotalItems);
            KindsText.Text = SentoryLocalization.Format(
                "KindsCountFormat",
                statistics.UrlItems,
                statistics.ImageItems);
            ImageBytesText.Text = FormatBytes(statistics.ImageBytes);
            FavoriteItemsText.Text =
                SentoryLocalization.Format(
                    "FavoritesPreservedFormat",
                    statistics.FavoriteItems);
        }
        catch (Exception)
        {
            StatusText.Text = SentoryLocalization.Text("StatisticsLoadFailed");
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
                ? SentoryLocalization.Text("DarkModeApplied")
                : SentoryLocalization.Text("LightModeApplied");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = SentoryLocalization.Text("ThemeSaveFailed");
        }
    }

    private void LanguageComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_initializing ||
            LanguageComboBox.SelectedItem is not
                SentoryLocalization.LanguageOption option)
        {
            return;
        }

        try
        {
            var settings = _settingsStore.Load();
            settings.Language = option.Code;
            _settingsStore.Save(settings);
            SentoryLocalization.Apply(
                System.Windows.Application.Current.Resources,
                settings.Language);
            LanguageChanged = true;
            RefreshLocalizedOptions(settings);
            VersionText.Text = SentoryLocalization.Format(
                "VersionFormat",
                GetVersionLabel());
            UpdateStartupControls();
            UpdateDiscordControls(settings.DiscordSupportEnabled);
            _ = RefreshStatisticsAsync();
            StatusText.Text = SentoryLocalization.Text("LanguageApplied");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = SentoryLocalization.Text("LanguageSaveFailed");
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
                ? SentoryLocalization.Text("StartupEnabled")
                : SentoryLocalization.Text("StartupDisabled");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or
                  System.Security.SecurityException or InvalidOperationException)
        {
            StatusText.Text = SentoryLocalization.Text("StartupChangeFailed");
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
                ? SentoryLocalization.Text("DiscordDetectionEnabled")
                : SentoryLocalization.Text("DiscordDetectionDisabled");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = SentoryLocalization.Text("DiscordSettingFailed");
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
        window.ShowDialog();
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
            HasDataChanged = result.Deleted.TotalItems > 0;
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

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DeleteNonFavoritesButton.IsEnabled = !busy;
        SaveAutoCleanupButton.IsEnabled = !busy;
        AutoCleanupComboBox.IsEnabled = !busy;
        OpenDataFolderButton.IsEnabled = !busy;
        if (busy)
        {
            StatusText.Text = SentoryLocalization.Text("CheckingCleanup");
        }
    }

    private void UpdateStartupControls()
    {
        try
        {
            var enabled = _startupManager.IsEnabled();
            StartupDescriptionText.Text = enabled
                ? SentoryLocalization.Text("StartupCurrentlyEnabled")
                : SentoryLocalization.Text("StartupCurrentlyDisabled");
            StartupToggleButton.Content = enabled
                ? SentoryLocalization.Text("TurnOff")
                : SentoryLocalization.Text("TurnOn");
        }
        catch (Exception)
        {
            StartupDescriptionText.Text =
                SentoryLocalization.Text("StartupStatusFailed");
            StartupToggleButton.Content = SentoryLocalization.Text("Retry");
        }
    }

    private void UpdateDiscordControls(bool enabled)
    {
        DiscordSupportToggleButton.Content = enabled
            ? SentoryLocalization.Text("InUse")
            : SentoryLocalization.Text("NotInUse");
        DiscordRepairButton.IsEnabled = enabled;
        DiscordStatusText.Text = !enabled
            ? SentoryLocalization.Text("DiscordNotInUse")
            : _discordRepairNeeded
                ? SentoryLocalization.Text("StateReconnect")
                : DiscordDetectionPresentation.GetLabel(_discordState);
    }

    private void RefreshLocalizedOptions(SentorySettings settings)
    {
        _initializing = true;
        try
        {
            var themeOptions = new[]
            {
                new ThemeOption(false, SentoryLocalization.Text("LightMode")),
                new ThemeOption(true, SentoryLocalization.Text("DarkMode"))
            };
            ThemeComboBox.ItemsSource = themeOptions;
            ThemeComboBox.SelectedItem = themeOptions.First(option =>
                option.IsDark == settings.IsDarkTheme);

            var cleanupOptions = new[]
            {
                new CleanupOption(0, SentoryLocalization.Text("CleanupOff")),
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

    private sealed record ThemeOption(bool IsDark, string Label)
    {
        public override string ToString() => Label;
    }
}
