using Sentory.Core;

namespace Sentory.Core.Tests;

public sealed class CaptureCoordinatorTests
{
    [Fact]
    public async Task GeneralTextDoesNotReachRepository()
    {
        var repository = new RecordingRepository();
        var coordinator = new CaptureCoordinator(repository);

        var results = await coordinator.CaptureUrlsAsync(
            Guid.NewGuid(),
            "일반 텍스트",
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVUrl,
            DeliveryStatus.NotObserved,
            "context",
            DateTimeOffset.UtcNow,
            ["kakao-input-validated"]);

        Assert.Empty(results);
        Assert.Empty(repository.Requests);
    }

    [Fact]
    public async Task MultipleUniqueMembersBecomeOneCollectionAndDuplicatesAreRemoved()
    {
        var repository = new RecordingRepository();
        var coordinator = new CaptureCoordinator(repository);
        byte[] imageBytes = [1, 2, 3];
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(imageBytes));

        var result = await coordinator.CaptureBatchAsync(
            Guid.NewGuid(),
            "https://example.com\nhttps://example.com/",
            [new ImageCapturePayload(imageBytes, hash, 1, 1, "image/png", ".png")],
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedImage,
            DeliveryStatus.Confirmed,
            "context",
            DateTimeOffset.UtcNow,
            ["test"]);

        Assert.NotNull(result);
        var request = Assert.Single(repository.Collections);
        Assert.Equal(2, request.Members.Count);
        Assert.Single(request.Members.Where(member => member.Kind == ContentKind.Url));
        Assert.Single(request.Members.Where(member => member.Kind == ContentKind.Image));
    }

    private sealed class RecordingRepository : ICaptureRepository
    {
        public List<UrlCaptureRequest> Requests { get; } = [];

        public List<CollectionCaptureRequest> Collections { get; } = [];

        public Task InitializeAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<CaptureResult> UpsertUrlAsync(
            UrlCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new CaptureResult(
                Guid.NewGuid(),
                true,
                true,
                1,
                0));
        }

        public Task<CaptureResult> UpsertImageAsync(
            ImageCaptureRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CaptureResult(
                Guid.NewGuid(),
                true,
                true,
                1,
                0));

        public Task<CaptureResult> UpsertCollectionAsync(
            CollectionCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            Collections.Add(request);
            return Task.FromResult(new CaptureResult(
                Guid.NewGuid(),
                true,
                true,
                1,
                1));
        }

        public Task<IReadOnlyList<CapturedItemSummary>> GetRecentAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CapturedItemSummary>>([]);

        public Task<bool> DeleteItemAsync(
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<BulkDeleteResult> DeleteItemsAsync(
            IReadOnlyCollection<Guid> itemIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BulkDeleteResult(
                itemIds.Count,
                0,
                itemIds.Count));

        public Task<bool> SetFavoriteAsync(
            Guid itemId,
            bool isFavorite,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> RecordCopyAsync(
            Guid itemId,
            DateTimeOffset copiedAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<StorageRepairResult> RepairStorageAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageRepairResult(0, 0, 0, 0));

        public Task<DataStatistics> GetDataStatisticsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DataStatistics(0, 0, 0, 0, 0));

        public Task<DataCleanupPreview> PreviewCleanupAsync(
            DateTimeOffset? olderThan,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DataCleanupPreview(0, 0, 0, 0));

        public Task<DataCleanupResult> CleanupAsync(
            DateTimeOffset? olderThan,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DataCleanupResult(
                new DataCleanupPreview(0, 0, 0, 0),
                0,
                0));

        public Task<IReadOnlyList<LinkPreviewCandidate>>
            GetLinkPreviewCandidatesAsync(
                int limit,
                DateTimeOffset retryBefore,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LinkPreviewCandidate>>([]);

        public Task<bool> UpdateLinkPreviewAsync(
            Guid itemId,
            LinkPreviewUpdate preview,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
