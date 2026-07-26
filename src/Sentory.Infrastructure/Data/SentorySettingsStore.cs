using System.IO;
using System.Text.Json;
using Sentory.Core;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Data;

public enum SentoryThemeMode
{
    Light,
    Dark,
    System
}

public sealed class SentorySettings
{
    public const int CurrentSyncStorageVersion = 2;
    public const int DefaultAutoFavoriteCopyThreshold = 3;
    public const int MinimumAutoFavoriteCopyThreshold = 2;
    public const int MaximumAutoFavoriteCopyThreshold = 5;

    private static readonly int[] SupportedCleanupDays = [0, 7, 30, 90, 180];
    private static readonly string[] SupportedLanguages =
        ["ko-KR", "en-US", "ja-JP", "zh-CN"];

    public string SortMode { get; set; } = "Newest";

    public string FilterDateRange { get; set; } = "All";

    public List<string> FilterSourceApps { get; set; } = [];

    public bool IsDarkTheme { get; set; }

    public string? ThemeMode { get; set; }

    public string Language { get; set; } = "ko-KR";

    public bool DiscordSupportEnabled { get; set; } = true;

    public bool KakaoTalkSupportEnabled { get; set; } = true;

    public bool? StartWithWindows { get; set; }

    public bool DiscordAccessibilityPrepared { get; set; }

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    public int AutoCleanupDays { get; set; }

    public DateTimeOffset? LastAutoCleanupAt { get; set; }

    public bool AutoFavoriteEnabled { get; set; }

    public int AutoFavoriteCopyThreshold { get; set; } =
        DefaultAutoFavoriteCopyThreshold;

    public DateTimeOffset? AutoFavoriteChangedAt { get; set; }

    public DateTimeOffset? LastUpdateCheckAt { get; set; }

    public bool SyncEnabled { get; set; }

    public string? SyncFolderPath { get; set; }

    public string? SyncDeviceId { get; set; }

    public int SyncStorageVersion { get; set; } = 1;

    public string? SyncMigrationDeviceId { get; set; }

    internal void Normalize()
    {
        ThemeMode = ResolveThemeMode(ThemeMode, IsDarkTheme).ToString();

        if (!SupportedLanguages.Contains(
                Language,
                StringComparer.OrdinalIgnoreCase))
        {
            Language = "ko-KR";
        }
        else
        {
            Language = SupportedLanguages.First(value =>
                string.Equals(
                    value,
                    Language,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!SupportedCleanupDays.Contains(AutoCleanupDays))
        {
            AutoCleanupDays = 0;
        }

        if (AutoFavoriteCopyThreshold is <
                MinimumAutoFavoriteCopyThreshold or >
                MaximumAutoFavoriteCopyThreshold)
        {
            AutoFavoriteCopyThreshold =
                DefaultAutoFavoriteCopyThreshold;
        }

        if (!Enum.TryParse<GalleryDateRange>(FilterDateRange, out _))
        {
            FilterDateRange = GalleryDateRange.All.ToString();
        }

        FilterSourceApps = (FilterSourceApps ?? [])
            .Where(value => Enum.TryParse<SourceApp>(value, out _))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!string.IsNullOrWhiteSpace(SyncFolderPath))
        {
            try
            {
                SyncFolderPath = Path.IsPathRooted(SyncFolderPath)
                    ? Path.GetFullPath(SyncFolderPath)
                    : null;
            }
            catch (Exception exception)
                when (exception is ArgumentException or
                      NotSupportedException or
                      PathTooLongException)
            {
                SyncFolderPath = null;
            }
        }
        else
        {
            SyncFolderPath = null;
        }

        if (SyncFolderPath is null)
        {
            SyncEnabled = false;
        }

        if (!SyncDeviceIdentity.IsValid(SyncDeviceId))
        {
            SyncDeviceId = null;
        }

        if (SyncStorageVersion is < 1 or > CurrentSyncStorageVersion)
        {
            SyncStorageVersion = 1;
        }

        if (!SyncDeviceIdentity.IsValid(SyncMigrationDeviceId))
        {
            SyncMigrationDeviceId = null;
        }

        if (SyncDeviceId is null)
        {
            SyncEnabled = false;
        }
    }

    public SentoryThemeMode GetThemeMode() =>
        ResolveThemeMode(ThemeMode, IsDarkTheme);

    private static SentoryThemeMode ResolveThemeMode(
        string? value,
        bool legacyIsDarkTheme) =>
        Enum.TryParse<SentoryThemeMode>(
            value,
            ignoreCase: true,
            out var mode)
            ? mode
            : legacyIsDarkTheme
                ? SentoryThemeMode.Dark
                : SentoryThemeMode.Light;
}

public sealed class SentorySettingsStore(SentoryDataPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SentorySettings Load()
    {
        try
        {
            if (!File.Exists(paths.SettingsPath))
            {
                return new SentorySettings();
            }

            var json = File.ReadAllText(paths.SettingsPath);
            var settings = JsonSerializer.Deserialize<SentorySettings>(
                               json,
                               JsonOptions) ??
                           new SentorySettings();
            settings.Normalize();
            return settings;
        }
        catch (JsonException)
        {
            QuarantineInvalidSettings();
            return new SentorySettings();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return new SentorySettings();
        }
    }

    public void Save(SentorySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        paths.EnsureDirectories();

        var temporaryPath =
            $"{paths.SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, paths.SettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void QuarantineInvalidSettings()
    {
        try
        {
            if (!File.Exists(paths.SettingsPath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(paths.SettingsPath) ??
                            paths.RootDirectory;
            var quarantinePath = Path.Combine(
                directory,
                $"gallery-settings.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.json");
            File.Move(paths.SettingsPath, quarantinePath);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
