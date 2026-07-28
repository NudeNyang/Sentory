namespace Sentory.Core;

public enum GallerySortMode
{
    Newest,
    Oldest,
    MostCaptured,
    MostCopied,
    RecentlyCopied,
    Name
}

public enum GalleryDateRange
{
    All,
    Today,
    Last7Days,
    Last30Days
}

public sealed record GalleryQueryOptions(
    ContentKind? Kind,
    string SearchText,
    GalleryDateRange DateRange,
    GallerySortMode SortMode,
    bool FavoritesOnly = false,
    IReadOnlySet<SourceApp>? SourceApps = null);

public static class GalleryQuery
{
    public static IReadOnlyList<CapturedItemSummary> Apply(
        IEnumerable<CapturedItemSummary> items,
        GalleryQueryOptions options,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        var search = options.SearchText.Trim();
        var filtered = items.Where(item =>
            MatchesKind(item, options.Kind) &&
            (!options.FavoritesOnly || item.IsFavorite) &&
            MatchesSource(item, options.SourceApps) &&
            IsInDateRange(item.LastCapturedAt, options.DateRange, now) &&
            MatchesSearch(item, search));

        return options.SortMode switch
        {
            GallerySortMode.Newest => filtered
                .OrderByDescending(item => item.LastCapturedAt)
                .ThenByDescending(item => item.CreatedAt)
                .ToArray(),
            GallerySortMode.Oldest => filtered
                .OrderBy(item => item.LastCapturedAt)
                .ThenBy(item => item.CreatedAt)
                .ToArray(),
            GallerySortMode.MostCaptured => filtered
                .OrderByDescending(item => item.CaptureCount)
                .ThenByDescending(item => item.LastCapturedAt)
                .ToArray(),
            GallerySortMode.MostCopied => filtered
                .OrderByDescending(item => item.CopyCount)
                .ThenByDescending(item => item.LastCopiedAt)
                .ThenByDescending(item => item.LastCapturedAt)
                .ToArray(),
            GallerySortMode.RecentlyCopied => filtered
                .OrderByDescending(item => item.LastCopiedAt.HasValue)
                .ThenByDescending(item => item.LastCopiedAt)
                .ThenByDescending(item => item.LastCapturedAt)
                .ToArray(),
            GallerySortMode.Name => filtered
                .OrderBy(GetNameKey, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(item => item.LastCapturedAt)
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                options.SortMode,
                "Unknown gallery sort mode.")
        };
    }

    private static bool MatchesSearch(
        CapturedItemSummary item,
        string search)
    {
        if (search.Length == 0)
        {
            return true;
        }

        return item.OriginalUrl.Contains(
                   search,
                   StringComparison.OrdinalIgnoreCase) ||
               item.NormalizedKey.Contains(
                   search,
                   StringComparison.OrdinalIgnoreCase) ||
               item.Domain.Contains(
                   search,
                   StringComparison.OrdinalIgnoreCase) ||
               (item.PageTitle?.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ?? false) ||
               (item.PageDescription?.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ?? false) ||
               (item.OcrDisplayName?.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ?? false) ||
               (item.OcrText?.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ?? false) ||
               (item.Members?.Any(member =>
                    member.OriginalUrl.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    member.NormalizedKey.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    member.Domain.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (member.OcrDisplayName?.Contains(
                        search,
                        StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (member.OcrText?.Contains(
                        search,
                        StringComparison.OrdinalIgnoreCase) ?? false)) ?? false);
    }

    private static bool MatchesKind(
        CapturedItemSummary item,
        ContentKind? kind) =>
        kind is null ||
        item.Kind == kind ||
        item.Kind == ContentKind.Collection &&
        (item.Members?.Any(member => member.Kind == kind) ?? false);

    private static bool MatchesSource(
        CapturedItemSummary item,
        IReadOnlySet<SourceApp>? selectedSources)
    {
        if (selectedSources is null || selectedSources.Count == 0)
        {
            return true;
        }

        return selectedSources.Contains(item.LastSourceApp);
    }

    private static bool IsInDateRange(
        DateTimeOffset capturedAt,
        GalleryDateRange range,
        DateTimeOffset now)
    {
        return range switch
        {
            GalleryDateRange.All => true,
            GalleryDateRange.Today =>
                capturedAt.LocalDateTime.Date == now.LocalDateTime.Date,
            GalleryDateRange.Last7Days =>
                capturedAt >= now.AddDays(-7),
            GalleryDateRange.Last30Days =>
                capturedAt >= now.AddDays(-30),
            _ => throw new ArgumentOutOfRangeException(
                nameof(range),
                range,
                "Unknown gallery date range.")
        };
    }

    private static string GetNameKey(CapturedItemSummary item) =>
        item.Kind switch
        {
            ContentKind.Url when !string.IsNullOrWhiteSpace(item.PageTitle) =>
                item.PageTitle,
            ContentKind.Url when !string.IsNullOrWhiteSpace(item.Domain) =>
                item.Domain,
            ContentKind.Image when !string.IsNullOrWhiteSpace(item.OcrDisplayName) =>
                item.OcrDisplayName,
            ContentKind.Image => "클립보드 이미지",
            ContentKind.Collection => item.Members is { Count: > 0 }
                ? $"묶음 {item.Members.Count}개"
                : "묶음",
            _ => item.NormalizedKey
        };
}
