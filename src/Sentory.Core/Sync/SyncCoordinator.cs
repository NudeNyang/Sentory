using System.Security.Cryptography;

namespace Sentory.Core.Sync;

public sealed record SyncRunResult(
    int Uploaded,
    int AlreadyUploaded,
    int Downloaded,
    int AlreadyApplied,
    int SequenceGaps);

public sealed class SyncCoordinator(
    ISyncOperationJournal journal,
    ISyncObjectStore objectStore)
{
    private const int DefaultPageSize = 200;
    private const int MaximumPages = 1000;
    private const int UploadBatchSize = 200;

    public async Task<SyncRunResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var uploaded = 0;
        var alreadyUploaded = 0;
        var downloaded = 0;
        var alreadyApplied = 0;
        var sequenceGaps = 0;

        var pending = await journal.GetUnpublishedAsync(
            UploadBatchSize,
            cancellationToken);
        foreach (var operation in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = SyncOperationSerializer.Serialize(operation);
            var sha256 = ComputeSha256(content);
            var result = await objectStore.PutIfAbsentAsync(
                SyncOperationObjectKey.Create(operation),
                content,
                sha256,
                cancellationToken);
            await journal.MarkPublishedAsync(
                operation.OperationId,
                cancellationToken);
            if (result == SyncPutResult.Created)
            {
                uploaded++;
            }
            else
            {
                alreadyUploaded++;
            }
        }

        var objects = await ListOperationObjectsAsync(cancellationToken);
        foreach (var group in objects
                     .Where(item => !string.Equals(
                         item.DeviceId,
                         journal.DeviceId,
                         StringComparison.Ordinal))
                     .GroupBy(item => item.DeviceId, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var conflictingSequence = group
                .GroupBy(item => item.Sequence)
                .FirstOrDefault(sequenceGroup =>
                    sequenceGroup.Select(item => item.OperationId)
                        .Distinct()
                        .Skip(1)
                        .Any());
            if (conflictingSequence is not null)
            {
                throw new InvalidDataException(
                    "같은 기기와 순번에 서로 다른 동기화 작업이 있습니다.");
            }

            var checkpointState = await journal.GetCheckpointAsync(
                group.Key,
                cancellationToken);
            var checkpoint = checkpointState.LastSequence;
            foreach (var item in group
                         .OrderBy(value => value.Sequence)
                         .ThenBy(value => value.OperationId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Sequence <= checkpoint)
                {
                    continue;
                }

                if (item.Sequence != checkpoint + 1)
                {
                    sequenceGaps++;
                    break;
                }

                var stored = await objectStore.TryGetAsync(
                    item.Info.Key,
                    cancellationToken) ??
                             throw new InvalidDataException(
                                 "목록에 있던 동기화 작업 파일을 찾을 수 없습니다.");
                ValidateStoredObject(item.Info, stored);
                var operation = SyncOperationSerializer.Deserialize(
                    stored.Content);
                ValidateObjectKey(operation, item);
                var applyResult = await journal.ApplyRemoteAsync(
                    operation,
                    cancellationToken);
                switch (applyResult)
                {
                    case SyncApplyResult.Applied:
                        downloaded++;
                        checkpoint = operation.Sequence;
                        break;
                    case SyncApplyResult.AlreadyApplied:
                        alreadyApplied++;
                        checkpoint = Math.Max(
                            checkpoint,
                            operation.Sequence);
                        break;
                    case SyncApplyResult.SequenceGap:
                        sequenceGaps++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (applyResult == SyncApplyResult.SequenceGap)
                {
                    break;
                }
            }
        }

        return new SyncRunResult(
            uploaded,
            alreadyUploaded,
            downloaded,
            alreadyApplied,
            sequenceGaps);
    }

    private async Task<IReadOnlyList<OperationObjectInfo>>
        ListOperationObjectsAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, OperationObjectInfo>(
            StringComparer.Ordinal);
        string? continuationToken = null;
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        for (var pageNumber = 0; pageNumber < MaximumPages; pageNumber++)
        {
            var page = await objectStore.ListAsync(
                SyncOperationObjectKey.OperationsPrefix,
                continuationToken,
                DefaultPageSize,
                cancellationToken);
            foreach (var item in page.Items)
            {
                if (SyncOperationObjectKey.TryParse(
                        item.Key,
                        out var deviceId,
                        out var sequence,
                        out var operationId))
                {
                    values.TryAdd(
                        item.Key,
                        new OperationObjectInfo(
                            item,
                            deviceId,
                            sequence,
                            operationId));
                }
            }

            if (page.ContinuationToken is null)
            {
                return values.Values.ToArray();
            }

            if (!seenTokens.Add(page.ContinuationToken))
            {
                throw new InvalidDataException(
                    "클라우드 목록의 이어받기 토큰이 반복되었습니다.");
            }

            continuationToken = page.ContinuationToken;
        }

        throw new InvalidDataException(
            "클라우드 동기화 작업 목록이 허용된 페이지 수를 넘었습니다.");
    }

    private static void ValidateStoredObject(
        SyncObjectInfo listed,
        SyncStoredObject stored)
    {
        if (!string.Equals(
                listed.Key,
                stored.Key,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "클라우드 동기화 작업 키가 목록과 일치하지 않습니다.");
        }

        if (listed.Size != stored.Content.LongLength)
        {
            throw new InvalidDataException(
                "클라우드 동기화 작업 크기가 목록과 일치하지 않습니다.");
        }

        var actualSha256 = ComputeSha256(stored.Content);
        if (!string.Equals(
                listed.Sha256,
                actualSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                stored.Sha256,
                actualSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "클라우드 동기화 작업 SHA-256이 일치하지 않습니다.");
        }
    }

    private static void ValidateObjectKey(
        SyncOperation operation,
        OperationObjectInfo item)
    {
        if (!string.Equals(
                operation.DeviceId,
                item.DeviceId,
                StringComparison.Ordinal) ||
            operation.Sequence != item.Sequence ||
            operation.OperationId != item.OperationId)
        {
            throw new InvalidDataException(
                "클라우드 경로와 동기화 작업 식별자가 일치하지 않습니다.");
        }
    }

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed record OperationObjectInfo(
        SyncObjectInfo Info,
        string DeviceId,
        long Sequence,
        Guid OperationId);
}
