using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class SyncItemProjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Sync.Item.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UrlAppearsOnceInSecondSqliteGallery()
    {
        var replicaA = await CreateReplicaAsync("a");
        var replicaB = await CreateReplicaAsync("b");
        var objectStore = new InMemorySyncObjectStore
        {
            ReturnDuplicateListEntries = true,
            ReverseListOrder = true
        };
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/article?utm_source=test&b=2&a=1",
            out var normalized));
        var captured = await replicaA.Captures.UpsertUrlAsync(
            new UrlCaptureRequest(
                Guid.NewGuid(),
                normalized.Original,
                normalized,
                SourceApp.Discord,
                CaptureMethod.DiscordConfirmedSend,
                DeliveryStatus.Confirmed,
                "discord-context",
                DateTimeOffset.Parse("2026-07-26T09:00:00+09:00"),
                ["newest-message-url-match"]));
        var localItem = Assert.Single(
            await replicaA.Captures.GetRecentAsync(10));
        var operation = await new SyncItemExportService(
            replicaA.Journal,
            objectStore,
            replicaA.Paths).ExportAsync(localItem);

        await TransferAsync(replicaA, replicaB, objectStore);
        var projector = new SyncItemProjectionService(
            replicaB.Captures,
            objectStore);
        var first = await projector.ProjectReceivedAsync(replicaB.Journal);
        var repeated = await projector.ProjectReceivedAsync(replicaB.Journal);
        var remoteItem = Assert.Single(
            await replicaB.Captures.GetRecentAsync(10));

        Assert.Equal(captured.ItemId, operation.ItemId);
        Assert.Equal(captured.ItemId, remoteItem.ItemId);
        Assert.Equal(normalized.Value, remoteItem.NormalizedKey);
        Assert.Equal(1, remoteItem.CaptureCount);
        Assert.Equal(1, remoteItem.ShareCount);
        Assert.Equal(1, first.Projected);
        Assert.Equal(0, first.AlreadyProjected);
        Assert.Equal(0, repeated.Projected);
        Assert.Equal(1, repeated.AlreadyProjected);
    }

    [Fact]
    public async Task ImageBlobAppearsOnceInSecondSqliteGallery()
    {
        var replicaA = await CreateReplicaAsync("a");
        var replicaB = await CreateReplicaAsync("b");
        var objectStore = new InMemorySyncObjectStore();
        byte[] imageBytes = [137, 80, 78, 71, 1, 2, 3, 4, 5];
        var sha256 = Convert.ToHexString(
            SHA256.HashData(imageBytes));
        var captured = await replicaA.Captures.UpsertImageAsync(
            new ImageCaptureRequest(
                Guid.NewGuid(),
                imageBytes,
                sha256,
                640,
                480,
                "image/png",
                ".png",
                SourceApp.KakaoTalk,
                CaptureMethod.KakaoCtrlVImage,
                DeliveryStatus.NotObserved,
                "kakao-context",
                DateTimeOffset.Parse("2026-07-26T09:10:00+09:00"),
                ["clipboard-image"],
                "capture.png"));
        var localItem = Assert.Single(
            await replicaA.Captures.GetRecentAsync(10));
        await new SyncItemExportService(
            replicaA.Journal,
            objectStore,
            replicaA.Paths).ExportAsync(localItem);

        await TransferAsync(replicaA, replicaB, objectStore);
        var projector = new SyncItemProjectionService(
            replicaB.Captures,
            objectStore);
        var first = await projector.ProjectReceivedAsync(replicaB.Journal);
        var repeated = await projector.ProjectReceivedAsync(replicaB.Journal);
        var remoteItem = Assert.Single(
            await replicaB.Captures.GetRecentAsync(10));
        var remotePath = Path.Combine(
            replicaB.Paths.RootDirectory,
            remoteItem.ContentPath!);

        Assert.Equal(captured.ItemId, remoteItem.ItemId);
        Assert.Equal(sha256.ToLowerInvariant(), remoteItem.Sha256);
        Assert.Equal(640, remoteItem.PixelWidth);
        Assert.Equal(480, remoteItem.PixelHeight);
        Assert.Equal(imageBytes, await File.ReadAllBytesAsync(remotePath));
        Assert.Equal(1, first.Projected);
        Assert.Equal(1, repeated.AlreadyProjected);
        Assert.Equal(1, remoteItem.CaptureCount);
    }

    [Fact]
    public async Task MissingImageBlobCanBeRetriedWithoutLosingOperation()
    {
        var replicaB = await CreateReplicaAsync("b");
        var objectStore = new InMemorySyncObjectStore();
        byte[] imageBytes = [10, 20, 30, 40];
        var sha256 = Convert.ToHexString(
            SHA256.HashData(imageBytes)).ToLowerInvariant();
        var payload = SyncItemPayload.CreateImage(
            new SyncImageContent(
                sha256,
                imageBytes.LongLength,
                100,
                80,
                "image/png",
                ".png",
                null),
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedImage,
            DeliveryStatus.Confirmed,
            "discord-context",
            DateTimeOffset.UtcNow,
            ["image-match"]);
        var operation = SyncOperation.Create(
            SyncDeviceIdentity.Create(),
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            SyncItemPayloadSerializer.Serialize(payload));
        objectStore.Seed(operation);
        await new SyncCoordinator(
            replicaB.Journal,
            objectStore).RunOnceAsync();
        var projector = new SyncItemProjectionService(
            replicaB.Captures,
            objectStore);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            projector.ProjectReceivedAsync(replicaB.Journal));
        Assert.Empty(await replicaB.Captures.GetRecentAsync(10));

        await objectStore.PutIfAbsentAsync(
            SyncBlobObjectKey.Create(sha256),
            imageBytes,
            sha256);
        var retried = await projector.ProjectReceivedAsync(
            replicaB.Journal);

        Assert.Equal(1, retried.Projected);
        Assert.Single(await replicaB.Captures.GetRecentAsync(10));
    }

    [Fact]
    public async Task CorruptedImageBlobDoesNotCreateGalleryItem()
    {
        var replicaB = await CreateReplicaAsync("b");
        var objectStore = new InMemorySyncObjectStore();
        byte[] imageBytes = [50, 60, 70, 80];
        var sha256 = Convert.ToHexString(
            SHA256.HashData(imageBytes)).ToLowerInvariant();
        var payload = SyncItemPayload.CreateImage(
            new SyncImageContent(
                sha256,
                imageBytes.LongLength,
                200,
                160,
                "image/png",
                ".png",
                null),
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedImage,
            DeliveryStatus.Confirmed,
            "discord-context",
            DateTimeOffset.UtcNow,
            ["image-match"]);
        var operation = SyncOperation.Create(
            SyncDeviceIdentity.Create(),
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            SyncItemPayloadSerializer.Serialize(payload));
        objectStore.Seed(operation);
        var blobKey = SyncBlobObjectKey.Create(sha256);
        await objectStore.PutIfAbsentAsync(
            blobKey,
            imageBytes,
            sha256);
        objectStore.Corrupt(blobKey);
        await new SyncCoordinator(
            replicaB.Journal,
            objectStore).RunOnceAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SyncItemProjectionService(
                replicaB.Captures,
                objectStore).ProjectReceivedAsync(replicaB.Journal));

        Assert.Empty(await replicaB.Captures.GetRecentAsync(10));
    }

    [Fact]
    public async Task ExistingUrlIsMergedInsteadOfDuplicated()
    {
        var replicaA = await CreateReplicaAsync("a");
        var replicaB = await CreateReplicaAsync("b");
        var objectStore = new InMemorySyncObjectStore();
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/shared",
            out var normalized));
        var localRequest = new UrlCaptureRequest(
            Guid.NewGuid(),
            normalized.Original,
            normalized,
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVUrl,
            DeliveryStatus.NotObserved,
            "context",
            DateTimeOffset.UtcNow,
            ["clipboard-url"]);
        await replicaA.Captures.UpsertUrlAsync(localRequest);
        var existing = await replicaB.Captures.UpsertUrlAsync(
            localRequest with
            {
                EventId = Guid.NewGuid()
            });
        await new SyncItemExportService(
            replicaA.Journal,
            objectStore,
            replicaA.Paths).ExportAsync(
                Assert.Single(
                    await replicaA.Captures.GetRecentAsync(10)));

        await TransferAsync(replicaA, replicaB, objectStore);
        await new SyncItemProjectionService(
            replicaB.Captures,
            objectStore).ProjectReceivedAsync(replicaB.Journal);
        var remoteItem = Assert.Single(
            await replicaB.Captures.GetRecentAsync(10));

        Assert.Equal(existing.ItemId, remoteItem.ItemId);
        Assert.Equal(2, remoteItem.CaptureCount);
    }

    [Fact]
    public async Task ExistingImageIsMergedInsteadOfDuplicated()
    {
        var replicaA = await CreateReplicaAsync("a");
        var replicaB = await CreateReplicaAsync("b");
        var objectStore = new InMemorySyncObjectStore();
        byte[] imageBytes = [1, 3, 5, 7, 9];
        var sha256 = Convert.ToHexString(
            SHA256.HashData(imageBytes));
        var request = new ImageCaptureRequest(
            Guid.NewGuid(),
            imageBytes,
            sha256,
            320,
            240,
            "image/png",
            ".png",
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVImage,
            DeliveryStatus.NotObserved,
            "context",
            DateTimeOffset.UtcNow,
            ["clipboard-image"]);
        await replicaA.Captures.UpsertImageAsync(request);
        var existing = await replicaB.Captures.UpsertImageAsync(
            request with
            {
                EventId = Guid.NewGuid()
            });
        await new SyncItemExportService(
            replicaA.Journal,
            objectStore,
            replicaA.Paths).ExportAsync(
                Assert.Single(
                    await replicaA.Captures.GetRecentAsync(10)));

        await TransferAsync(replicaA, replicaB, objectStore);
        await new SyncItemProjectionService(
            replicaB.Captures,
            objectStore).ProjectReceivedAsync(replicaB.Journal);
        var remoteItem = Assert.Single(
            await replicaB.Captures.GetRecentAsync(10));

        Assert.Equal(existing.ItemId, remoteItem.ItemId);
        Assert.Equal(2, remoteItem.CaptureCount);
        Assert.Equal(
            imageBytes,
            await File.ReadAllBytesAsync(
                Path.Combine(
                    replicaB.Paths.RootDirectory,
                    remoteItem.ContentPath!)));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static async Task TransferAsync(
        Replica replicaA,
        Replica replicaB,
        InMemorySyncObjectStore objectStore)
    {
        await new SyncCoordinator(
            replicaA.Journal,
            objectStore).RunOnceAsync();
        await new SyncCoordinator(
            replicaB.Journal,
            objectStore).RunOnceAsync();
    }

    private async Task<Replica> CreateReplicaAsync(string name)
    {
        var paths = SentoryDataPaths.ForRoot(
            Path.Combine(_root, name));
        var captures = new SqliteCaptureRepository(paths);
        await captures.InitializeAsync();
        var journal = new SqliteSyncOperationJournal(
            paths,
            SyncDeviceIdentity.Create());
        await journal.InitializeAsync();
        return new Replica(paths, captures, journal);
    }

    private sealed record Replica(
        SentoryDataPaths Paths,
        SqliteCaptureRepository Captures,
        SqliteSyncOperationJournal Journal);
}
