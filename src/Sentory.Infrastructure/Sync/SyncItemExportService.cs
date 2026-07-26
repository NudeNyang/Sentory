using System.Security.Cryptography;
using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Sync;

public sealed record SyncItemExportBatchResult(
    int Exported,
    int ChangedDuringExport);

public sealed class SyncItemExportService(
    ISyncOperationJournal journal,
    ISyncObjectStore objectStore,
    SentoryDataPaths paths)
{
    public async Task<SyncOperation> ExportAsync(
        CapturedItemSummary item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var candidate = ToCandidate(item);
        var payload = await CreatePayloadAsync(
            candidate,
            cancellationToken);
        var content = SyncItemPayloadSerializer.Serialize(payload);
        return await journal.AppendLocalAsync(
            item.ItemId,
            SyncOperationKind.Upsert,
            item.LastCapturedAt,
            content,
            cancellationToken);
    }

    public async Task<SyncItemExportBatchResult> ExportPendingAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (journal is not ISyncItemExportJournal exportJournal)
        {
            throw new InvalidOperationException(
                "동기화 저널이 항목 내보내기 기록을 지원하지 않습니다.");
        }

        var exported = 0;
        var changedDuringExport = 0;
        var candidates = await exportJournal.GetPendingItemExportsAsync(
            limit,
            cancellationToken);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = await CreatePayloadAsync(
                candidate,
                cancellationToken);
            var operation = await exportJournal.AppendLocalItemExportAsync(
                candidate,
                SyncItemPayloadSerializer.Serialize(payload),
                cancellationToken);
            if (operation is null)
            {
                changedDuringExport++;
            }
            else
            {
                exported++;
            }
        }

        return new SyncItemExportBatchResult(
            exported,
            changedDuringExport);
    }

    private async Task<SyncItemPayload> CreatePayloadAsync(
        SyncItemExportCandidate item,
        CancellationToken cancellationToken) =>
        item.Kind switch
        {
            ContentKind.Url => CreateUrlPayload(item),
            ContentKind.Image => await CreateImagePayloadAsync(
                item,
                cancellationToken),
            _ => throw new NotSupportedException(
                "현재 동기화는 단일 URL과 사진 항목만 내보낼 수 있습니다.")
        };

    private static SyncItemPayload CreateUrlPayload(
        SyncItemExportCandidate item)
    {
        if (string.IsNullOrWhiteSpace(item.OriginalUrl) ||
            string.IsNullOrWhiteSpace(item.NormalizedKey) ||
            string.IsNullOrWhiteSpace(item.Domain))
        {
            throw new InvalidDataException(
                "URL 항목에 동기화할 주소 정보가 없습니다.");
        }

        if (!UrlNormalizer.TryNormalize(
                item.OriginalUrl,
                out var normalized) ||
            !string.Equals(
                normalized.Value,
                item.NormalizedKey,
                StringComparison.Ordinal) ||
            !string.Equals(
                normalized.Domain,
                item.Domain,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "URL 항목의 정규화 정보가 현재 규칙과 일치하지 않습니다.");
        }

        return SyncItemPayload.CreateUrl(
            new SyncUrlContent(
            item.OriginalUrl,
            item.NormalizedKey,
            item.Domain),
            item.SourceApp,
            item.CaptureMethod,
            item.DeliveryStatus,
            CreateContextHash(item.ItemId),
            item.LastCapturedAt,
            ["sentory-sync-export"]);
    }

    private async Task<SyncItemPayload> CreateImagePayloadAsync(
        SyncItemExportCandidate item,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.ContentPath) ||
            string.IsNullOrWhiteSpace(item.Sha256) ||
            string.IsNullOrWhiteSpace(item.MimeType) ||
            item.PixelWidth is null ||
            item.PixelHeight is null)
        {
            throw new InvalidDataException(
                "사진 항목에 동기화할 콘텐츠 정보가 없습니다.");
        }

        var absolutePath = ResolveImagePath(item.ContentPath);
        var fileInfo = new FileInfo(absolutePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException(
                "동기화할 로컬 사진을 찾을 수 없습니다.",
                absolutePath);
        }

        if (fileInfo.Length <= 0 ||
            fileInfo.Length > SyncItemPayload.MaximumImageBytes)
        {
            throw new InvalidDataException(
                "동기화할 사진 크기가 허용 범위를 벗어났습니다.");
        }

        var content = await File.ReadAllBytesAsync(
            absolutePath,
            cancellationToken);
        var actualSha256 = ComputeSha256(content);
        if (!string.Equals(
                actualSha256,
                item.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "로컬 사진의 SHA-256이 보관함 정보와 일치하지 않습니다.");
        }

        var extension = Path.GetExtension(absolutePath).ToLowerInvariant();
        var blobKey = objectStore is IReadableSyncObjectStore readableStore
            ? readableStore.CreateImageObjectKey(
                actualSha256,
                extension)
            : SyncBlobObjectKey.Create(actualSha256);
        await objectStore.PutIfAbsentAsync(
            blobKey,
            content,
            actualSha256,
            cancellationToken);
        return SyncItemPayload.CreateImage(
            new SyncImageContent(
                actualSha256,
                content.LongLength,
                item.PixelWidth.Value,
                item.PixelHeight.Value,
                item.MimeType,
                extension,
                string.IsNullOrWhiteSpace(item.OriginalUrl)
                    ? null
                    : item.OriginalUrl),
            item.SourceApp,
            item.CaptureMethod,
            item.DeliveryStatus,
            CreateContextHash(item.ItemId),
            item.LastCapturedAt,
            ["sentory-sync-export"]);
    }

    private static SyncItemExportCandidate ToCandidate(
        CapturedItemSummary item) =>
        new(
            item.ItemId,
            item.Kind,
            item.OriginalUrl,
            item.NormalizedKey,
            item.Domain,
            item.LastSourceApp,
            item.LastCaptureMethod,
            item.DeliveryStatus,
            item.CreatedAt,
            item.LastCapturedAt,
            item.ContentPath,
            item.Sha256,
            item.MimeType,
            item.PixelWidth,
            item.PixelHeight);

    private string ResolveImagePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException(
                "사진 콘텐츠 경로는 데이터 폴더의 상대 경로여야 합니다.");
        }

        var imagesRoot = Path.GetFullPath(paths.ImagesDirectory);
        var candidate = Path.GetFullPath(
            Path.Combine(paths.RootDirectory, relativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootWithSeparator = Path.EndsInDirectorySeparator(imagesRoot)
            ? imagesRoot
            : string.Concat(imagesRoot, Path.DirectorySeparatorChar);
        if (!candidate.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidDataException(
                "사진 콘텐츠 경로가 Sentory 사진 폴더를 벗어났습니다.");
        }

        return candidate;
    }

    private static string CreateContextHash(Guid itemId) =>
        $"sync-item:{itemId:N}";

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
