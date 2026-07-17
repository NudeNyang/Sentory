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
    DateTimeOffset? LastCopiedAt = null);

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
}
