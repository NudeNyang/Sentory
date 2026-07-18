namespace Sentory.Core;

public enum SourceApp
{
    Discord,
    KakaoTalk
}

public enum ContentKind
{
    Url,
    Image,
    File
}

public enum DeliveryStatus
{
    Confirmed,
    NotObserved
}

public enum CaptureMethod
{
    DiscordConfirmedSend,
    KakaoCtrlVUrl,
    KakaoCtrlVImage,
    KakaoTypedUrl,
    KakaoFileDialog,
    KakaoDragDrop
}

public enum LinkPreviewStatus
{
    Available,
    Unavailable
}

public sealed record UrlCaptureRequest(
    Guid EventId,
    string OriginalUrl,
    NormalizedUrl NormalizedUrl,
    SourceApp SourceApp,
    CaptureMethod CaptureMethod,
    DeliveryStatus DeliveryStatus,
    string ContextHash,
    DateTimeOffset CapturedAt,
    IReadOnlyList<string> ConfirmationSignals);

public sealed record ImageCaptureRequest(
    Guid EventId,
    ReadOnlyMemory<byte> PngBytes,
    string Sha256,
    int PixelWidth,
    int PixelHeight,
    SourceApp SourceApp,
    CaptureMethod CaptureMethod,
    DeliveryStatus DeliveryStatus,
    string ContextHash,
    DateTimeOffset CapturedAt,
    IReadOnlyList<string> ConfirmationSignals);

public sealed record CaptureResult(
    Guid ItemId,
    bool ItemCreated,
    bool EventApplied,
    int CaptureCount,
    int ShareCount);

public sealed record CapturedItemSummary(
    Guid ItemId,
    ContentKind Kind,
    string OriginalUrl,
    string NormalizedKey,
    string Domain,
    SourceApp LastSourceApp,
    CaptureMethod LastCaptureMethod,
    DeliveryStatus DeliveryStatus,
    int CaptureCount,
    int ShareCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastCapturedAt,
    string? ContentPath = null,
    string? Sha256 = null,
    bool IsFavorite = false,
    int CopyCount = 0,
    DateTimeOffset? LastCopiedAt = null,
    string? PageTitle = null,
    string? PageDescription = null,
    string? SiteIconPath = null,
    string? PreviewImagePath = null,
    LinkPreviewStatus? PreviewStatus = null,
    DateTimeOffset? PreviewFetchedAt = null);

public sealed record LinkPreviewCandidate(
    Guid ItemId,
    string Url,
    string NormalizedKey);

public sealed record LinkPreviewUpdate(
    LinkPreviewStatus Status,
    string? PageTitle,
    string? PageDescription,
    string? SiteIconPath,
    string? PreviewImagePath,
    DateTimeOffset FetchedAt);

public sealed record StorageRepairResult(
    int OrphanFilesDeleted,
    int TemporaryFilesDeleted,
    int MissingImageFiles,
    int FileDeleteFailures);

public sealed record DataStatistics(
    int TotalItems,
    int FavoriteItems,
    int UrlItems,
    int ImageItems,
    long ImageBytes);

public sealed record DataCleanupPreview(
    int TotalItems,
    int UrlItems,
    int ImageItems,
    long ImageBytes);

public sealed record DataCleanupResult(
    DataCleanupPreview Deleted,
    int DeletedImageFiles,
    int FileDeleteFailures);

public interface ICaptureRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<CaptureResult> UpsertUrlAsync(
        UrlCaptureRequest request,
        CancellationToken cancellationToken = default);

    Task<CaptureResult> UpsertImageAsync(
        ImageCaptureRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CapturedItemSummary>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteItemAsync(
        Guid itemId,
        CancellationToken cancellationToken = default);

    Task<bool> SetFavoriteAsync(
        Guid itemId,
        bool isFavorite,
        CancellationToken cancellationToken = default);

    Task<bool> RecordCopyAsync(
        Guid itemId,
        DateTimeOffset copiedAt,
        CancellationToken cancellationToken = default);

    Task<StorageRepairResult> RepairStorageAsync(
        CancellationToken cancellationToken = default);

    Task<DataStatistics> GetDataStatisticsAsync(
        CancellationToken cancellationToken = default);

    Task<DataCleanupPreview> PreviewCleanupAsync(
        DateTimeOffset? olderThan,
        CancellationToken cancellationToken = default);

    Task<DataCleanupResult> CleanupAsync(
        DateTimeOffset? olderThan,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LinkPreviewCandidate>> GetLinkPreviewCandidatesAsync(
        int limit,
        DateTimeOffset retryBefore,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateLinkPreviewAsync(
        Guid itemId,
        LinkPreviewUpdate preview,
        CancellationToken cancellationToken = default);
}
