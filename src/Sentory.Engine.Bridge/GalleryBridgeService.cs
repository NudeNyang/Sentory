using Sentory.Core;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Ocr;
using System.Security.Cryptography;
using System.Text;

namespace Sentory.Engine.Bridge;

public sealed class GalleryBridgeService(
    ICaptureRepository repository,
    SentoryDataPaths paths)
{
    public const int ProtocolVersion = 2;
    public const int DefaultPageSize = 80;
    public const int MaximumPageSize = 200;

    public async Task<GallerySnapshotDto> GetGalleryAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, MaximumPageSize);
        var items = await repository.GetRecentAsync(
            safeLimit,
            cancellationToken);
        return new GallerySnapshotDto(
            ProtocolVersion,
            items.Count,
            items.Select(item => GalleryCardProjection.Create(item, paths))
                .ToArray());
    }

    public async Task<GalleryRevisionDto> GetRevisionAsync(
        CancellationToken cancellationToken = default)
    {
        var latest = (await repository.GetRecentAsync(1, cancellationToken))
            .FirstOrDefault();
        return latest is null
            ? new GalleryRevisionDto(null, null)
            : new GalleryRevisionDto(
                latest.ItemId.ToString("N"),
                latest.LastCapturedAt);
    }

    public async Task<GallerySnapshotDto> GetGalleryPageAsync(
        GalleryPageRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (repository is not IGalleryPageRepository pages)
        {
            throw new NotSupportedException(
                "현재 C# 엔진은 갤러리 페이지 조회를 지원하지 않습니다.");
        }

        var kind = ParseOptionalEnum<ContentKind>(request.Kind, nameof(request.Kind));
        var dateRange = ParseEnum<GalleryDateRange>(
            request.DateRange,
            GalleryDateRange.All,
            nameof(request.DateRange));
        var sortMode = ParseEnum<GallerySortMode>(
            request.SortMode,
            GallerySortMode.Newest,
            nameof(request.SortMode));
        var sources = (request.SourceApps ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ParseEnum<SourceApp>(value, null, nameof(request.SourceApps)))
            .ToHashSet();
        var page = await pages.GetGalleryPageAsync(
            new GalleryPageRequest(
                new GalleryQueryOptions(
                    kind,
                    request.SearchText ?? string.Empty,
                    dateRange,
                    sortMode,
                    request.FavoritesOnly,
                    sources),
                Math.Max(0, request.Offset),
                Math.Clamp(request.Limit, 1, MaximumPageSize),
                DateTimeOffset.Now),
            cancellationToken);
        return new GallerySnapshotDto(
            ProtocolVersion,
            page.Total,
            page.Items.Select(item => GalleryCardProjection.Create(item, paths))
                .ToArray());
    }

    public async Task<GalleryItemDetailDto?> GetItemAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var id = ParseItemId(itemId);
        if (repository is not IGalleryItemRepository items)
        {
            throw new NotSupportedException(
                "현재 C# 엔진은 항목 상세 조회를 지원하지 않습니다.");
        }
        var item = await items.GetGalleryItemAsync(id, cancellationToken);
        return item is null ? null : GalleryCardProjection.CreateDetail(item, paths);
    }

    public async Task<GalleryMutationDto> SetFavoriteAsync(
        string itemId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        var id = ParseItemId(itemId);
        var changed = await repository.SetFavoriteAsync(
            id,
            isFavorite,
            cancellationToken);
        return new GalleryMutationDto(changed, changed ? 1 : 0, 0, isFavorite);
    }

    public async Task<GalleryMutationDto> DeleteItemsAsync(
        IReadOnlyList<string> itemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var ids = itemIds
            .Take(MaximumPageSize * 10)
            .Select(ParseItemId)
            .Distinct()
            .ToArray();
        var result = await repository.DeleteItemsAsync(ids, cancellationToken);
        return new GalleryMutationDto(
            result.DeletedItems > 0,
            result.DeletedItems,
            result.MissingItems);
    }

    public async Task<GalleryMutationDto> RecordCopyAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var id = ParseItemId(itemId);
        if (repository is not IGalleryItemRepository items)
        {
            throw new NotSupportedException(
                "현재 C# 엔진은 항목 조회를 지원하지 않습니다.");
        }
        var item = await items.GetGalleryItemAsync(id, cancellationToken);
        if (item is null || !await repository.RecordCopyAsync(
                id,
                DateTimeOffset.Now,
                cancellationToken))
        {
            return new GalleryMutationDto(false, 0, 1);
        }

        var copyCount = item.CopyCount + 1;
        var isFavorite = item.IsFavorite;
        var settings = new SentorySettingsStore(paths).Load();
        if (settings.AutoFavoriteEnabled &&
            !isFavorite &&
            copyCount >= settings.AutoFavoriteCopyThreshold &&
            item.Kind is ContentKind.Url or ContentKind.Image)
        {
            isFavorite = await repository.SetFavoriteAsync(
                id,
                true,
                cancellationToken);
        }
        return new GalleryMutationDto(
            true,
            1,
            0,
            isFavorite,
            copyCount);
    }

    private static Guid ParseItemId(string itemId) =>
        Guid.TryParse(itemId, out var parsed)
            ? parsed
            : throw new ArgumentException("올바르지 않은 항목 ID입니다.", nameof(itemId));

    private static TEnum? ParseOptionalEnum<TEnum>(
        string? value,
        string parameterName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return ParseEnum<TEnum>(value, null, parameterName);
    }

    private static TEnum ParseEnum<TEnum>(
        string? value,
        TEnum? fallback,
        string parameterName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) && fallback is { } defaultValue)
        {
            return defaultValue;
        }
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }
        throw new ArgumentException(
            $"지원하지 않는 {parameterName} 값입니다: {value}",
            parameterName);
    }
}

public sealed record GalleryPageRequestDto(
    int Offset,
    int Limit,
    string? Kind,
    string? SearchText,
    string? DateRange,
    string? SortMode,
    bool FavoritesOnly,
    IReadOnlyList<string>? SourceApps);

public sealed record GalleryMutationDto(
    bool Success,
    int Changed,
    int Missing,
    bool? IsFavorite = null,
    int? CopyCount = null);

public sealed record GalleryItemDetailDto(
    GalleryCardDto Card,
    string? ContentPath,
    IReadOnlyList<GalleryMemberDto> Members);

public sealed record GalleryMemberDto(
    string Kind,
    string Title,
    string OriginalUrl,
    string Domain,
    string? ContentPath,
    string? MimeType);

public sealed record GallerySnapshotDto(
    int ProtocolVersion,
    int Total,
    IReadOnlyList<GalleryCardDto> Items);

public sealed record GalleryRevisionDto(
    string? LatestItemId,
    DateTimeOffset? LastCapturedAt);

public sealed record GalleryCardDto(
    string ItemId,
    string Kind,
    string Title,
    string Subtitle,
    string TypeLabel,
    string DateLabel,
    string StatusLabel,
    string OriginalUrl,
    string Domain,
    string SourceApp,
    DateTimeOffset LastCapturedAt,
    string? ArtworkPath,
    string ArtworkMode,
    string? SiteIconPath,
    bool IsFavorite,
    int CopyCount,
    int CaptureCount,
    DateTimeOffset? LastCopiedAt);

public static class GalleryCardProjection
{
    public static GalleryCardDto Create(
        CapturedItemSummary item,
        SentoryDataPaths paths)
    {
        var isImage = item.Kind == ContentKind.Image;
        var isCollection = item.Kind == ContentKind.Collection;
        var members = item.Members ?? [];
        var imageCount = members.Count(member => member.Kind == ContentKind.Image);
        var urlCount = members.Count(member => member.Kind == ContentKind.Url);
        var title = isCollection
            ? $"사진 {imageCount}개 · 링크 {urlCount}개"
            : isImage
                ? OcrTitleGenerator.CreateBestDisplayTitle(
                    item.OriginalUrl,
                    item.OcrDisplayName) ?? "클립보드 이미지"
                : !string.IsNullOrWhiteSpace(item.PageTitle)
                    ? item.PageTitle
                    : string.IsNullOrWhiteSpace(item.Domain)
                        ? "저장된 링크"
                        : item.Domain;
        var subtitle = isCollection
            ? CreateCollectionSubtitle(members)
            : isImage
                ? !string.IsNullOrWhiteSpace(item.OcrText)
                    ? CreateSnippet(item.OcrText)
                    : $"{GetImageFormatLabel(item)} 이미지"
                : !string.IsNullOrWhiteSpace(item.PageDescription)
                    ? item.PageDescription
                    : item.OriginalUrl;
        var artwork = ResolveArtwork(item, paths);
        return new GalleryCardDto(
            item.ItemId.ToString("N"),
            item.Kind.ToString(),
            title,
            subtitle,
            $"{GetKindLabel(item.Kind)} · {GetSourceLabel(item.LastSourceApp)}",
            $"{item.LastCapturedAt.LocalDateTime:M월 d일} · " +
            item.LastCapturedAt.LocalDateTime.ToString("HH:mm"),
            item.DeliveryStatus == DeliveryStatus.NotObserved
                ? "입력 시 저장됨"
                : "전송 시 저장됨",
            item.OriginalUrl,
            item.Domain,
            item.LastSourceApp.ToString(),
            item.LastCapturedAt,
            artwork.Path,
            artwork.Mode,
            ResolveStoredPath(item.SiteIconPath, paths),
            item.IsFavorite,
            item.CopyCount,
            item.CaptureCount,
            item.LastCopiedAt);
    }

    public static GalleryItemDetailDto CreateDetail(
        CapturedItemSummary item,
        SentoryDataPaths paths)
    {
        var members = (item.Members ?? [])
            .Select(member => new GalleryMemberDto(
                member.Kind.ToString(),
                member.Kind == ContentKind.Image
                    ? OcrTitleGenerator.CreateBestDisplayTitle(
                        member.OriginalUrl,
                        member.OcrDisplayName) ?? "이미지"
                    : string.IsNullOrWhiteSpace(member.Domain)
                        ? member.OriginalUrl
                        : member.Domain,
                member.OriginalUrl,
                member.Domain,
                ResolveStoredPath(member.ContentPath, paths),
                member.MimeType))
            .ToArray();
        return new GalleryItemDetailDto(
            Create(item, paths),
            ResolveStoredPath(item.ContentPath, paths),
            members);
    }

    private static (string? Path, string Mode) ResolveArtwork(
        CapturedItemSummary item,
        SentoryDataPaths paths)
    {
        string? relativePath;
        string mode;
        if (item.Kind == ContentKind.Image)
        {
            relativePath = item.ContentPath;
            mode = "contain";
        }
        else if (item.Kind == ContentKind.Collection)
        {
            relativePath = item.Members?
                .FirstOrDefault(member =>
                    member.Kind == ContentKind.Image &&
                    !string.IsNullOrWhiteSpace(member.ContentPath))?
                .ContentPath;
            relativePath ??= item.PreviewImagePath;
            mode = "contain";
        }
        else if (!string.IsNullOrWhiteSpace(item.PreviewImagePath))
        {
            relativePath = item.PreviewImagePath;
            mode = "cover";
        }
        else
        {
            relativePath = item.SiteIconPath;
            mode = "icon";
        }

        var resolved = ResolveStoredPath(relativePath, paths);
        if (resolved is not null && mode == "contain")
        {
            resolved = ResolveExistingCardThumbnail(resolved, paths) ?? resolved;
        }
        return (resolved, mode);
    }

    internal static string? ResolveExistingCardThumbnail(
        string sourcePath,
        SentoryDataPaths paths)
    {
        var source = new FileInfo(Path.GetFullPath(sourcePath));
        if (!source.Exists)
        {
            return null;
        }
        var cacheKey = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{source.FullName}|{source.Length}|{source.LastWriteTimeUtc.Ticks}")))
            .ToLowerInvariant();
        var cachePath = Path.Combine(
            paths.RootDirectory,
            "cache",
            "gallery-card-thumbnails",
            "v3",
            $"{cacheKey}.jpg");
        return File.Exists(cachePath) ? Path.GetFullPath(cachePath) : null;
    }

    internal static string? ResolveStoredPath(
        string? relativePath,
        SentoryDataPaths paths)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var root = Path.GetFullPath(paths.RootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(
            Path.Combine(paths.RootDirectory, relativePath));
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
               File.Exists(target)
            ? target
            : null;
    }

    private static string CreateCollectionSubtitle(
        IReadOnlyList<CapturedCollectionMember> members)
    {
        var domains = string.Join(
            " · ",
            members.Where(member => member.Kind == ContentKind.Url)
                .Select(member => member.Domain)
                .Where(domain => !string.IsNullOrWhiteSpace(domain))
                .Take(2));
        return domains.Length > 0 ? domains : $"항목 {members.Count}개";
    }

    private static string CreateSnippet(string value)
    {
        var normalized = string.Join(
            " ",
            value.Split(
                ['\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries));
        return normalized.Length <= 90
            ? normalized
            : normalized[..90].TrimEnd() + "…";
    }

    private static string GetImageFormatLabel(CapturedItemSummary item)
    {
        var extension = Path.GetExtension(item.ContentPath);
        return !string.IsNullOrWhiteSpace(extension)
            ? extension.TrimStart('.').ToUpperInvariant()
            : item.MimeType?.Split('/').LastOrDefault()?.ToUpperInvariant()
              ?? "PNG";
    }

    private static string GetKindLabel(ContentKind kind) => kind switch
    {
        ContentKind.Image => "사진",
        ContentKind.Collection => "모음",
        _ => "링크"
    };

    private static string GetSourceLabel(SourceApp source) => source switch
    {
        SourceApp.KakaoTalk => "카카오톡",
        SourceApp.Line => "LINE",
        _ => source.ToString()
    };
}
