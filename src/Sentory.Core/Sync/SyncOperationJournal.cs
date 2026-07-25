namespace Sentory.Core.Sync;

public enum SyncApplyResult
{
    Applied,
    AlreadyApplied,
    SequenceGap
}

public sealed record SyncCheckpoint(
    string RemoteDeviceId,
    long LastSequence,
    DateTimeOffset? UpdatedAt);

public interface ISyncOperationJournal
{
    string DeviceId { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<SyncOperation> AppendLocalAsync(
        Guid itemId,
        SyncOperationKind kind,
        DateTimeOffset occurredAt,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncOperation>> GetUnpublishedAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task MarkPublishedAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<SyncCheckpoint> GetCheckpointAsync(
        string remoteDeviceId,
        CancellationToken cancellationToken = default);

    Task<SyncApplyResult> ApplyRemoteAsync(
        SyncOperation operation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncOperation>> GetReceivedAsync(
        CancellationToken cancellationToken = default);
}
