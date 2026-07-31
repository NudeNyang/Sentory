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
    public const string AutomaticLanguage = "auto";
    public const string FolderSyncProvider = "Folder";
    public const string WebDavSyncProvider = "WebDav";
    public const int CurrentLanguageSettingVersion = 1;
    public const int CurrentSyncStorageVersion = 2;
    public const bool DefaultAutoFavoriteEnabled = true;
    public const int DefaultAutoFavoriteCopyThreshold = 3;
    public const int MinimumAutoFavoriteCopyThreshold = 2;
    public const int MaximumAutoFavoriteCopyThreshold = 5;

    private static readonly int[] SupportedCleanupDays = [0, 7, 30, 90, 180];
    private static readonly string[] SupportedLanguages =
        [AutomaticLanguage, "ko-KR", "en-US", "ja-JP", "zh-CN"];

    public string SortMode { get; set; } = "Newest";

    public string FilterDateRange { get; set; } = "All";

    public List<string> FilterSourceApps { get; set; } = [];

    public bool IsDarkTheme { get; set; }

    public string? ThemeMode { get; set; }

    public string Language { get; set; } = AutomaticLanguage;

    public int LanguageSettingVersion { get; set; }

    public bool MessengerDetectionSetupCompleted { get; set; }

    public bool DiscordSupportEnabled { get; set; }

    public bool KakaoTalkSupportEnabled { get; set; }

    public bool SlackSupportEnabled { get; set; }

    public bool WhatsAppSupportEnabled { get; set; }

    public bool TelegramSupportEnabled { get; set; }

    public bool LineSupportEnabled { get; set; }

    public bool WeChatSupportEnabled { get; set; }

    public bool? StartWithWindows { get; set; }

    public bool DiscordAccessibilityPrepared { get; set; }

    public bool DiscordAutoRestartConsentGranted { get; set; }

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    public int AutoCleanupDays { get; set; }

    public DateTimeOffset? LastAutoCleanupAt { get; set; }

    public bool AutoFavoriteEnabled { get; set; } =
        DefaultAutoFavoriteEnabled;

    public int AutoFavoriteCopyThreshold { get; set; } =
        DefaultAutoFavoriteCopyThreshold;

    public DateTimeOffset? AutoFavoriteChangedAt { get; set; }

    public DateTimeOffset? LastUpdateCheckAt { get; set; }

    public bool SyncEnabled { get; set; }

    public string SyncProvider { get; set; } = FolderSyncProvider;

    public string? SyncFolderPath { get; set; }

    public string? SyncWebDavEndpoint { get; set; }

    public string? SyncWebDavUsername { get; set; }

    public string? SyncWebDavProtectedPassword { get; set; }

    public string? SyncDeviceId { get; set; }

    public int SyncStorageVersion { get; set; } = 1;

    public string? SyncMigrationDeviceId { get; set; }

    public string? SyncStoreId { get; set; }

    internal void Normalize()
    {
        ThemeMode = ResolveThemeMode(ThemeMode, IsDarkTheme).ToString();

        if (LanguageSettingVersion < CurrentLanguageSettingVersion)
        {
            if (string.Equals(
                    Language,
                    "ko-KR",
                    StringComparison.OrdinalIgnoreCase))
            {
                Language = AutomaticLanguage;
            }

            LanguageSettingVersion = CurrentLanguageSettingVersion;
        }

        if (!SupportedLanguages.Contains(
                Language,
                StringComparer.OrdinalIgnoreCase))
        {
            Language = AutomaticLanguage;
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

        if (!AutoFavoriteEnabled &&
            AutoFavoriteChangedAt is null &&
            AutoFavoriteCopyThreshold ==
                DefaultAutoFavoriteCopyThreshold)
        {
            AutoFavoriteEnabled = true;
        }

        if (!Enum.TryParse<GalleryDateRange>(FilterDateRange, out _))
        {
            FilterDateRange = GalleryDateRange.All.ToString();
        }

        FilterSourceApps = (FilterSourceApps ?? [])
            .Where(value => Enum.TryParse<SourceApp>(value, out _))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        SyncProvider = string.Equals(
            SyncProvider,
            WebDavSyncProvider,
            StringComparison.OrdinalIgnoreCase)
            ? WebDavSyncProvider
            : FolderSyncProvider;

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

        SyncWebDavEndpoint = NormalizeWebDavEndpoint(
            SyncWebDavEndpoint);
        SyncWebDavUsername = string.IsNullOrWhiteSpace(
            SyncWebDavUsername)
            ? null
            : SyncWebDavUsername.Trim();
        SyncWebDavProtectedPassword = string.IsNullOrWhiteSpace(
            SyncWebDavProtectedPassword)
            ? null
            : SyncWebDavProtectedPassword;

        if ((SyncProvider == FolderSyncProvider &&
             SyncFolderPath is null) ||
            (SyncProvider == WebDavSyncProvider &&
             SyncWebDavEndpoint is null))
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

        if (!IsValidSyncStoreId(SyncStoreId))
        {
            SyncStoreId = null;
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

    private static bool IsValidSyncStoreId(string? value) =>
        value is not null &&
        value.Length == 32 &&
        string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal) &&
        Guid.TryParseExact(value, "N", out _);

    private static string? NormalizeWebDavEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
                ? uri.AbsolutePath
                : string.Concat(uri.AbsolutePath, "/")
        };
        return builder.Uri.AbsoluteUri;
    }
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
            MigrateLegacyMessengerDetectionSettings(json, settings);
            MigrateDiscordAutoRestartConsent(json, settings);
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

    private static void MigrateLegacyMessengerDetectionSettings(
        string json,
        SentorySettings settings)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (HasProperty(
                root,
                nameof(SentorySettings.MessengerDetectionSetupCompleted)))
        {
            return;
        }

        settings.MessengerDetectionSetupCompleted = true;
        PreserveLegacyDefault(
            root,
            nameof(SentorySettings.DiscordSupportEnabled),
            value => settings.DiscordSupportEnabled = value);
        PreserveLegacyDefault(
            root,
            nameof(SentorySettings.KakaoTalkSupportEnabled),
            value => settings.KakaoTalkSupportEnabled = value);
        PreserveLegacyDefault(
            root,
            nameof(SentorySettings.SlackSupportEnabled),
            value => settings.SlackSupportEnabled = value);
        PreserveLegacyDefault(
            root,
            nameof(SentorySettings.WhatsAppSupportEnabled),
            value => settings.WhatsAppSupportEnabled = value);
        PreserveLegacyDefault(
            root,
            nameof(SentorySettings.TelegramSupportEnabled),
            value => settings.TelegramSupportEnabled = value);
        PreserveLegacyDefault(
            root,
            nameof(SentorySettings.LineSupportEnabled),
            value => settings.LineSupportEnabled = value);
        PreserveLegacyDefault(
            root,
            nameof(SentorySettings.WeChatSupportEnabled),
            value => settings.WeChatSupportEnabled = value);
    }

    private static void MigrateDiscordAutoRestartConsent(
        string json,
        SentorySettings settings)
    {
        using var document = JsonDocument.Parse(json);
        if (HasProperty(
                document.RootElement,
                nameof(SentorySettings.DiscordAutoRestartConsentGranted)))
        {
            return;
        }

        settings.DiscordAutoRestartConsentGranted =
            settings.DiscordSupportEnabled ||
            settings.DiscordAccessibilityPrepared;
    }

    private static void PreserveLegacyDefault(
        JsonElement root,
        string propertyName,
        Action<bool> apply)
    {
        if (!HasProperty(root, propertyName))
        {
            apply(true);
        }
    }

    private static bool HasProperty(
        JsonElement root,
        string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.EnumerateObject().Any(property =>
            string.Equals(
                property.Name,
                propertyName,
                StringComparison.OrdinalIgnoreCase));
}
