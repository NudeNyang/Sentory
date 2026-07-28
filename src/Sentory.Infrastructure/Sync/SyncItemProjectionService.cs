using System.Security.Cryptography;
using Sentory.Core;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Sync;

public sealed record SyncItemProjectionResult(
    int Projected,
    int AlreadyProjected,
    int Skipped,
    int Pending);

public sealed class SyncItemProjectionService(
    ICaptureRepository captureRepository,
    ISyncObjectStore objectStore)
{
    public async Task<SyncItemProjectionResult> ProjectReceivedAsync(
        ISyncOperationJournal journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var projected = 0;
        var alreadyProjected = 0;
        var skipped = 0;
        var pending = 0;
        var operations = await journal.GetReceivedAsync(cancellationToken);
        var deletionTimes = new Dictionary<string, DateTimeOffset>(
            StringComparer.Ordinal);
        if (journal is ISyncItemExportJournal deletionJournal)
        {
            var deletions = await deletionJournal.GetDeletionOperationsAsync(
                cancellationToken);
            foreach (var deletion in deletions)
            {
                var payload = SyncItemPayloadSerializer.Deserialize(
                    deletion.Payload);
                var normalizedKey = GetNormalizedKey(payload);
                if (!deletionTimes.TryGetValue(
                        normalizedKey,
                        out var existing) ||
                    deletion.OccurredAt > existing)
                {
                    deletionTimes[normalizedKey] = deletion.OccurredAt;
                }
            }
        }

        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.Kind is not (
                    SyncOperationKind.Upsert or SyncOperationKind.Delete))
            {
                skipped++;
                continue;
            }

            var payload = SyncItemPayloadSerializer.Deserialize(
                operation.Payload);
            var normalizedKey = GetNormalizedKey(payload);
            if (operation.Kind == SyncOperationKind.Delete)
            {
                if (captureRepository is not ISyncItemDeletionRepository
                    deletionRepository)
                {
                    throw new InvalidOperationException(
                        "보관함 저장소가 동기화 삭제를 지원하지 않습니다.");
                }

                var deleted = await deletionRepository
                    .ApplySyncedDeletionAsync(
                        normalizedKey,
                        operation.OccurredAt,
                        cancellationToken);
                if (deleted)
                {
                    projected++;
                }
                else
                {
                    alreadyProjected++;
                }

                continue;
            }

            if (deletionTimes.TryGetValue(
                    normalizedKey,
                    out var deletedAt) &&
                deletedAt >= payload.CapturedAt)
            {
                skipped++;
                continue;
            }

            if (!TryParseProjectionEnums(
                    payload,
                    out var sourceApp,
                    out var captureMethod,
                    out var deliveryStatus))
            {
                pending++;
                continue;
            }

            CaptureResult? result = payload.ContentKind switch
            {
                SyncItemContentKinds.Url => await ProjectUrlAsync(
                    operation,
                    payload,
                    sourceApp,
                    captureMethod,
                    deliveryStatus,
                    cancellationToken),
                SyncItemContentKinds.Image => await ProjectImageAsync(
                    operation,
                    payload,
                    sourceApp,
                    captureMethod,
                    deliveryStatus,
                    cancellationToken),
                _ => throw new NotSupportedException(
                    "지원하지 않는 동기화 콘텐츠 종류입니다.")
            };
            if (result is null)
            {
                pending++;
                continue;
            }

            if (result.EventApplied)
            {
                projected++;
            }
            else
            {
                alreadyProjected++;
            }

            if (journal is ISyncItemExportJournal exportJournal)
            {
                await exportJournal.MarkRemoteItemProjectedAsync(
                    result.ItemId,
                    payload.CapturedAt,
                    operation.OperationId,
                    cancellationToken);
            }
        }

        return new SyncItemProjectionResult(
            projected,
            alreadyProjected,
            skipped,
            pending);
    }

    private async Task<CaptureResult> ProjectUrlAsync(
        SyncOperation operation,
        SyncItemPayload payload,
        SourceApp sourceApp,
        CaptureMethod captureMethod,
        DeliveryStatus deliveryStatus,
        CancellationToken cancellationToken)
    {
        var url = payload.Url ??
                  throw new InvalidDataException(
                      "URL 동기화 본문에 URL 정보가 없습니다.");
        if (!UrlNormalizer.TryNormalize(
                url.OriginalUrl,
                out var normalized) ||
            !string.Equals(
                normalized.Value,
                url.NormalizedUrl,
                StringComparison.Ordinal) ||
            !string.Equals(
                normalized.Domain,
                url.Domain,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "원격 URL 정규화 정보가 현재 규칙과 일치하지 않습니다.");
        }

        return await captureRepository.UpsertUrlAsync(
            new UrlCaptureRequest(
                operation.OperationId,
                url.OriginalUrl,
                normalized,
                sourceApp,
                captureMethod,
                deliveryStatus,
                payload.ContextHash,
                payload.CapturedAt,
                payload.ConfirmationSignals,
                operation.ItemId),
            cancellationToken);
    }

    private async Task<CaptureResult?> ProjectImageAsync(
        SyncOperation operation,
        SyncItemPayload payload,
        SourceApp sourceApp,
        CaptureMethod captureMethod,
        DeliveryStatus deliveryStatus,
        CancellationToken cancellationToken)
    {
        var image = payload.Image ??
                    throw new InvalidDataException(
                        "사진 동기화 본문에 사진 정보가 없습니다.");
        var expectedSha256 = image.ContentSha256.ToLowerInvariant();
        var key = objectStore is IReadableSyncObjectStore readableStore
            ? readableStore.CreateImageObjectKey(
                expectedSha256,
                image.FileExtension)
            : SyncBlobObjectKey.Create(expectedSha256);
        var stored = await objectStore.TryGetAsync(
            key,
            cancellationToken);
        if (stored is null && objectStore is IReadableSyncObjectStore)
        {
            key = SyncBlobObjectKey.Create(expectedSha256);
            stored = await objectStore.TryGetAsync(
                key,
                cancellationToken);
        }

        if (stored is null)
        {
            return null;
        }
        if (!string.Equals(stored.Key, key, StringComparison.Ordinal) ||
            stored.Content.LongLength != image.ByteSize)
        {
            throw new InvalidDataException(
                "원격 사진 블롭의 키 또는 크기가 일치하지 않습니다.");
        }

        var actualSha256 = ComputeSha256(stored.Content);
        if (!string.Equals(
                actualSha256,
                expectedSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                stored.Sha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "원격 사진 블롭의 SHA-256이 일치하지 않습니다.");
        }

        return await captureRepository.UpsertImageAsync(
            new ImageCaptureRequest(
                operation.OperationId,
                stored.Content,
                expectedSha256,
                image.PixelWidth,
                image.PixelHeight,
                image.MimeType,
                image.FileExtension,
                sourceApp,
                captureMethod,
                deliveryStatus,
                payload.ContextHash,
                payload.CapturedAt,
                payload.ConfirmationSignals,
                image.OriginalFileName,
                operation.ItemId),
            cancellationToken);
    }

    private static bool TryParseProjectionEnums(
        SyncItemPayload payload,
        out SourceApp sourceApp,
        out CaptureMethod captureMethod,
        out DeliveryStatus deliveryStatus)
    {
        var hasKnownSource = TryParseEnum(
            payload.SourceApp,
            out sourceApp);
        var hasKnownCaptureMethod = TryParseEnum(
            payload.CaptureMethod,
            out captureMethod);
        var hasKnownDeliveryStatus = TryParseEnum(
            payload.DeliveryStatus,
            out deliveryStatus);
        return hasKnownSource &&
               hasKnownCaptureMethod &&
               hasKnownDeliveryStatus;
    }

    private static bool TryParseEnum<T>(string value, out T parsed)
        where T : struct, Enum
    {
        return Enum.TryParse(
                value,
                ignoreCase: false,
                out parsed) &&
            Enum.IsDefined(parsed);
    }

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string GetNormalizedKey(SyncItemPayload payload) =>
        payload.ContentKind switch
        {
            SyncItemContentKinds.Url => payload.Url?.NormalizedUrl ??
                throw new InvalidDataException(
                    "URL 동기화 본문에 URL 정보가 없습니다."),
            SyncItemContentKinds.Image => string.Concat(
                "sha256:",
                payload.Image?.ContentSha256.ToLowerInvariant() ??
                throw new InvalidDataException(
                    "사진 동기화 본문에 사진 정보가 없습니다.")),
            _ => throw new NotSupportedException(
                "지원하지 않는 동기화 콘텐츠 종류입니다.")
        };
}
