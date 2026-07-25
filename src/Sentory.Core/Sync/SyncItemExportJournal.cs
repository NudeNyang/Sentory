namespace Sentory.Core.Sync;

public sealed record SyncItemExportCandidate(
    Guid ItemId,
    ContentKind Kind,
    string OriginalUrl,
    string NormalizedKey,
    string Domain,
    SourceApp SourceApp,
    CaptureMethod CaptureMethod,
    DeliveryStatus DeliveryStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastCapturedAt,
    string? ContentPath,
    string? Sha256,
    string? MimeType,
    int? PixelWidth,
    int? PixelHeight);

public interface ISyncItemExportJournal
{
    Task<IReadOnlyList<SyncItemExportCandidate>>
        GetPendingItemExportsAsync(
            int limit,
            CancellationToken cancellationToken = default);

    Task<SyncOperation?> AppendLocalItemExportAsync(
        SyncItemExportCandidate candidate,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);

    Task MarkRemoteItemProjectedAsync(
        Guid localItemId,
        DateTimeOffset lastCapturedAt,
        Guid remoteOperationId,
        CancellationToken cancellationToken = default);
}
