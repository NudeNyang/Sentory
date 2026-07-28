using Sentory.Infrastructure.Data;

namespace Sentory.App;

internal static class GallerySettingsSavePolicy
{
    public static SentorySettings Merge(
        SentorySettings current,
        SentorySettings gallerySettings)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(gallerySettings);

        current.SortMode = gallerySettings.SortMode;
        current.FilterDateRange = gallerySettings.FilterDateRange;
        current.FilterSourceApps = [.. gallerySettings.FilterSourceApps];
        current.IsDarkTheme = gallerySettings.IsDarkTheme;
        current.ThemeMode = gallerySettings.ThemeMode;
        current.Language = gallerySettings.Language;
        current.WindowLeft = gallerySettings.WindowLeft;
        current.WindowTop = gallerySettings.WindowTop;
        current.WindowWidth = gallerySettings.WindowWidth;
        current.WindowHeight = gallerySettings.WindowHeight;
        current.WindowMaximized = gallerySettings.WindowMaximized;
        return current;
    }
}
