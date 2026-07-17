using System.IO;
using System.Text.Json;

namespace Sentory.Infrastructure.Data;

public sealed class SentorySettings
{
    public string SortMode { get; set; } = "Newest";

    public bool IsDarkTheme { get; set; }

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }
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
            return JsonSerializer.Deserialize<SentorySettings>(
                       json,
                       JsonOptions) ??
                   new SentorySettings();
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  JsonException)
        {
            return new SentorySettings();
        }
    }

    public void Save(SentorySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
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
}
