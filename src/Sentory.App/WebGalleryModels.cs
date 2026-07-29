using Sentory.Core;

namespace Sentory.App;

internal sealed record WebGalleryClientMessage(
    string? Type,
    long Revision = 0,
    int Start = 0,
    int Count = 0);

internal sealed record WebGalleryCardDto(
    int Index,
    string ItemId,
    string Kind,
    string Title,
    string Subtitle,
    string TypeLabel,
    string DateLabel,
    string StatusLabel,
    string Domain,
    string Initial,
    string? ArtworkUrl,
    string ArtworkMode,
    string CollectionBadge,
    bool HasCollectionBadge,
    bool IsFavorite,
    bool HasBeenCopied,
    string CopyUsageLabel);

internal sealed record WebGalleryMediaSource(
    string AbsolutePath,
    bool CreateCardThumbnail);

internal sealed record WebGalleryArtworkCandidate(
    string RelativePath,
    string Mode,
    bool CreateCardThumbnail);

internal static class WebGalleryArtworkPolicy
{
    public static WebGalleryArtworkCandidate? Resolve(
        CapturedItemSummary item)
    {
        if (item.Kind == ContentKind.Image)
        {
            return string.IsNullOrWhiteSpace(item.ContentPath)
                ? null
                : new WebGalleryArtworkCandidate(
                    item.ContentPath,
                    "contain",
                    CreateCardThumbnail: true);
        }

        if (item.Kind == ContentKind.Collection)
        {
            var collectionImage = item.Members?
                .FirstOrDefault(member =>
                    member.Kind == ContentKind.Image &&
                    !string.IsNullOrWhiteSpace(member.ContentPath));
            if (collectionImage?.ContentPath is { Length: > 0 } imagePath)
            {
                return new WebGalleryArtworkCandidate(
                    imagePath,
                    "contain",
                    CreateCardThumbnail: true);
            }
        }

        if (!string.IsNullOrWhiteSpace(item.PreviewImagePath))
        {
            return new WebGalleryArtworkCandidate(
                item.PreviewImagePath,
                "cover",
                CreateCardThumbnail: false);
        }

        return string.IsNullOrWhiteSpace(item.SiteIconPath)
            ? null
            : new WebGalleryArtworkCandidate(
                item.SiteIconPath,
                "icon",
                CreateCardThumbnail: false);
    }
}

internal static class WebGalleryRangePolicy
{
    public const int MaximumRangeCount = 120;

    public static (int Start, int Count) Clamp(
        int start,
        int count,
        int total)
    {
        if (total <= 0)
        {
            return (0, 0);
        }

        var safeStart = Math.Clamp(start, 0, total);
        var safeCount = Math.Clamp(count, 0, MaximumRangeCount);
        return (safeStart, Math.Min(safeCount, total - safeStart));
    }
}
