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

    private sealed class RecordingRepository : ICaptureRepository
    {
        public List<UrlCaptureRequest> Requests { get; } = [];

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

        public Task<IReadOnlyList<CapturedItemSummary>> GetRecentAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CapturedItemSummary>>([]);

        public Task<bool> DeleteItemAsync(
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

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
    }
}
