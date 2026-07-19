using System.IO;
using System.Text.Json;
using Sentory.Core;

namespace Sentory.Infrastructure.Data;

public sealed class SentorySettings
{
    private static readonly int[] SupportedCleanupDays = [0, 30, 90, 180];
    private static readonly string[] SupportedLanguages =
        ["ko-KR", "en-US", "ja-JP", "zh-CN"];

    public string SortMode { get; set; } = "Newest";

    public string FilterDateRange { get; set; } = "All";

    public List<string> FilterSourceApps { get; set; } = [];

    public bool IsDarkTheme { get; set; }

    public string Language { get; set; } = "ko-KR";

    public bool DiscordSupportEnabled { get; set; } = true;

    public bool DiscordAccessibilityPrepared { get; set; }

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    public int AutoCleanupDays { get; set; }

    public DateTimeOffset? LastAutoCleanupAt { get; set; }

    internal void Normalize()
    {
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

        if (!Enum.TryParse<GalleryDateRange>(FilterDateRange, out _))
        {
            FilterDateRange = GalleryDateRange.All.ToString();
        }

        FilterSourceApps = (FilterSourceApps ?? [])
            .Where(value => Enum.TryParse<SourceApp>(value, out _))
            .Distinct(StringComparer.Ordinal)
            .ToList();
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
