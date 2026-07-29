using Sentory.Core;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Ocr;

namespace Sentory.Engine.Bridge;

public sealed class GalleryBridgeService(
    ICaptureRepository repository,
    SentoryDataPaths paths)
{
    public const int ProtocolVersion = 1;
    public const int DefaultPageSize = 500;
    public const int MaximumPageSize = 2_000;

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
}

public sealed record GallerySnapshotDto(
    int ProtocolVersion,
    int Total,
    IReadOnlyList<GalleryCardDto> Items);

public sealed record GalleryCardDto(
    string ItemId,
    string Kind,
    string Title,
    string Subtitle,
    string TypeLabel,
    string DateLabel,
    string StatusLabel,
    string Domain,
    string SourceApp,
    DateTimeOffset LastCapturedAt,
    string? ArtworkPath,
    string ArtworkMode,
    bool IsFavorite,
    int CopyCount);

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
            item.Domain,
            item.LastSourceApp.ToString(),
            item.LastCapturedAt,
            artwork.Path,
            artwork.Mode,
            item.IsFavorite,
            item.CopyCount);
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

        return (ResolveStoredPath(relativePath, paths), mode);
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
