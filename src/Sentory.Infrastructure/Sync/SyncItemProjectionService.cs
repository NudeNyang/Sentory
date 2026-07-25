using System.Security.Cryptography;
using Sentory.Core;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Sync;

public sealed record SyncItemProjectionResult(
    int Projected,
    int AlreadyProjected,
    int Skipped);

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
        var operations = await journal.GetReceivedAsync(cancellationToken);
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.Kind != SyncOperationKind.Upsert)
            {
                skipped++;
                continue;
            }

            var payload = SyncItemPayloadSerializer.Deserialize(
                operation.Payload);
            var result = payload.ContentKind switch
            {
                SyncItemContentKinds.Url => await ProjectUrlAsync(
                    operation,
                    payload,
                    cancellationToken),
                SyncItemContentKinds.Image => await ProjectImageAsync(
                    operation,
                    payload,
                    cancellationToken),
                _ => throw new NotSupportedException(
                    "지원하지 않는 동기화 콘텐츠 종류입니다.")
            };
            if (result.EventApplied)
            {
                projected++;
            }
            else
            {
                alreadyProjected++;
            }
        }

        return new SyncItemProjectionResult(
            projected,
            alreadyProjected,
            skipped);
    }

    private async Task<CaptureResult> ProjectUrlAsync(
        SyncOperation operation,
        SyncItemPayload payload,
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
                ParseEnum<SourceApp>(payload.SourceApp, "출처"),
                ParseEnum<CaptureMethod>(
                    payload.CaptureMethod,
                    "캡처 방식"),
                ParseEnum<DeliveryStatus>(
                    payload.DeliveryStatus,
                    "전송 상태"),
                payload.ContextHash,
                payload.CapturedAt,
                payload.ConfirmationSignals,
                operation.ItemId),
            cancellationToken);
    }

    private async Task<CaptureResult> ProjectImageAsync(
        SyncOperation operation,
        SyncItemPayload payload,
        CancellationToken cancellationToken)
    {
        var image = payload.Image ??
                    throw new InvalidDataException(
                        "사진 동기화 본문에 사진 정보가 없습니다.");
        var expectedSha256 = image.ContentSha256.ToLowerInvariant();
        var key = SyncBlobObjectKey.Create(expectedSha256);
        var stored = await objectStore.TryGetAsync(
            key,
            cancellationToken) ??
                     throw new InvalidDataException(
                         "원격 사진 블롭을 찾을 수 없습니다.");
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
                ParseEnum<SourceApp>(payload.SourceApp, "출처"),
                ParseEnum<CaptureMethod>(
                    payload.CaptureMethod,
                    "캡처 방식"),
                ParseEnum<DeliveryStatus>(
                    payload.DeliveryStatus,
                    "전송 상태"),
                payload.ContextHash,
                payload.CapturedAt,
                payload.ConfirmationSignals,
                image.OriginalFileName,
                operation.ItemId),
            cancellationToken);
    }

    private static T ParseEnum<T>(string value, string fieldName)
        where T : struct, Enum
    {
        if (!Enum.TryParse<T>(
                value,
                ignoreCase: false,
                out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new InvalidDataException(
                $"원격 항목의 {fieldName} 값을 지원하지 않습니다.");
        }

        return parsed;
    }

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
