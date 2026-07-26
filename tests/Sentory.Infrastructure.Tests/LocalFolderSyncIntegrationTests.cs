using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class LocalFolderSyncIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Local.Sync.Integration.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SharedFolderTransfersUrlAndImageBetweenTwoGalleries()
    {
        var replicaA = await CreateReplicaAsync("a");
        var replicaB = await CreateReplicaAsync("b");
        var sharedFolder = Path.Combine(_root, "shared");
        var storeA = new LocalFolderSyncObjectStore(sharedFolder);
        var storeB = new LocalFolderSyncObjectStore(sharedFolder);
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/shared?a=1&utm_source=test",
            out var normalized));
        var urlResult = await replicaA.Captures.UpsertUrlAsync(
            new UrlCaptureRequest(
                Guid.NewGuid(),
                normalized.Original,
                normalized,
                SourceApp.Discord,
                CaptureMethod.DiscordConfirmedSend,
                DeliveryStatus.Confirmed,
                "discord-context",
                DateTimeOffset.Parse("2026-07-26T10:00:00+09:00"),
                ["url-match"]));
        byte[] imageBytes = [137, 80, 78, 71, 10, 20, 30, 40];
        var imageSha256 = Convert.ToHexString(
            SHA256.HashData(imageBytes));
        var imageResult = await replicaA.Captures.UpsertImageAsync(
            new ImageCaptureRequest(
                Guid.NewGuid(),
                imageBytes,
                imageSha256,
                800,
                600,
                "image/png",
                ".png",
                SourceApp.KakaoTalk,
                CaptureMethod.KakaoCtrlVImage,
                DeliveryStatus.NotObserved,
                "kakao-context",
                DateTimeOffset.Parse("2026-07-26T10:01:00+09:00"),
                ["clipboard-image"],
                "shared.png"));
        var localItems = await replicaA.Captures.GetRecentAsync(10);
        var exporter = new SyncItemExportService(
            replicaA.Journal,
            storeA,
            replicaA.Paths);
        foreach (var item in localItems.OrderBy(item => item.CreatedAt))
        {
            await exporter.ExportAsync(item);
        }

        var cycleA = CreateCycle(replicaA, storeA);
        var cycleB = CreateCycle(replicaB, storeB);
        await cycleA.RunOnceAsync();
        var received = await cycleB.RunOnceAsync();
        var repeated = await cycleB.RunOnceAsync();
        var remoteItems = await replicaB.Captures.GetRecentAsync(10);
        var remoteUrl = Assert.Single(
            remoteItems,
            item => item.Kind == ContentKind.Url);
        var remoteImage = Assert.Single(
            remoteItems,
            item => item.Kind == ContentKind.Image);

        Assert.Equal(2, received.Transfer.Downloaded);
        Assert.Equal(2, received.Projection.Projected);
        Assert.Equal(0, repeated.Transfer.Downloaded);
        Assert.Equal(2, repeated.Projection.AlreadyProjected);
        Assert.Equal(urlResult.ItemId, remoteUrl.ItemId);
        Assert.Equal(imageResult.ItemId, remoteImage.ItemId);
        Assert.Equal(normalized.Value, remoteUrl.NormalizedKey);
        Assert.Equal(
            imageBytes,
            await File.ReadAllBytesAsync(
                Path.Combine(
                    replicaB.Paths.RootDirectory,
                    remoteImage.ContentPath!)));

        var restartedJournal = new SqliteSyncOperationJournal(
            replicaB.Paths,
            replicaB.Journal.DeviceId);
        await restartedJournal.InitializeAsync();
        var restartedReplica = replicaB with
        {
            Journal = restartedJournal
        };
        var afterRestart = await CreateCycle(
            restartedReplica,
            new LocalFolderSyncObjectStore(sharedFolder))
            .RunOnceAsync();

        Assert.Equal(0, afterRestart.Transfer.Downloaded);
        Assert.Equal(2, afterRestart.Projection.AlreadyProjected);
        Assert.Equal(
            2,
            (await replicaB.Captures.GetRecentAsync(10)).Count);
    }

    [Fact]
    public async Task AutomaticRuntimeDoesNotEchoRemoteItemBackToSource()
    {
        var replicaA = await CreateReplicaAsync("a");
        var replicaB = await CreateReplicaAsync("b");
        var sharedFolder = Path.Combine(_root, "shared-runtime");
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/no-echo",
            out var normalized));
        await replicaA.Captures.UpsertUrlAsync(
            new UrlCaptureRequest(
                Guid.NewGuid(),
                normalized.Original,
                normalized,
                SourceApp.Discord,
                CaptureMethod.DiscordConfirmedSend,
                DeliveryStatus.Confirmed,
                "context",
                DateTimeOffset.Parse("2026-07-26T11:30:00+09:00"),
                ["url-match"]));
        var runtimeA = new LocalFolderSyncRuntimeService(
            replicaA.Paths,
            replicaA.Captures);
        var runtimeB = new LocalFolderSyncRuntimeService(
            replicaB.Paths,
            replicaB.Captures);

        var source = await runtimeA.RunOnceAsync(
            replicaA.Journal.DeviceId,
            sharedFolder);
        var destination = await runtimeB.RunOnceAsync(
            replicaB.Journal.DeviceId,
            sharedFolder);
        var destinationRepeated = await runtimeB.RunOnceAsync(
            replicaB.Journal.DeviceId,
            sharedFolder);
        var sourceRepeated = await runtimeA.RunOnceAsync(
            replicaA.Journal.DeviceId,
            sharedFolder);

        Assert.Equal(1, source.Export.Exported);
        Assert.Equal(1, source.Publish.Uploaded);
        Assert.Equal(1, destination.Cycle.Transfer.Downloaded);
        Assert.Equal(1, destination.Cycle.Projection.Projected);
        Assert.Equal(0, destination.Export.Exported);
        Assert.Equal(0, destinationRepeated.Export.Exported);
        Assert.Equal(0, sourceRepeated.Cycle.Transfer.Downloaded);
        Assert.Equal(
            1,
            Assert.Single(
                await replicaA.Captures.GetRecentAsync(10)).CaptureCount);
        Assert.Equal(
            1,
            Assert.Single(
                await replicaB.Captures.GetRecentAsync(10)).CaptureCount);
    }

    [Fact]
    public async Task AutomaticRuntimePublishesReadablePhotoAndLinkFiles()
    {
        var replica = await CreateReplicaAsync("readable");
        var replicaB = await CreateReplicaAsync("readable-b");
        var sharedFolder = Path.Combine(_root, "shared-readable");
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/mobile-share",
            out var normalized));
        await replica.Captures.UpsertUrlAsync(
            new UrlCaptureRequest(
                Guid.NewGuid(),
                normalized.Original,
                normalized,
                SourceApp.Discord,
                CaptureMethod.DiscordConfirmedSend,
                DeliveryStatus.Confirmed,
                "context",
                DateTimeOffset.Parse("2026-07-26T18:00:00+09:00"),
                ["url-match"]));
        byte[] imageBytes = [137, 80, 78, 71, 13, 10, 26, 10, 4, 5, 6];
        var imageSha256 = Convert.ToHexString(
            SHA256.HashData(imageBytes));
        await replica.Captures.UpsertImageAsync(
            new ImageCaptureRequest(
                Guid.NewGuid(),
                imageBytes,
                imageSha256,
                320,
                200,
                "image/png",
                ".png",
                SourceApp.KakaoTalk,
                CaptureMethod.KakaoCtrlVImage,
                DeliveryStatus.NotObserved,
                "context",
                DateTimeOffset.Parse("2026-07-26T18:01:00+09:00"),
                ["clipboard-image"],
                "mobile.png"));

        var result = await new LocalFolderSyncRuntimeService(
            replica.Paths,
            replica.Captures).RunOnceAsync(
                replica.Journal.DeviceId,
                sharedFolder);
        var received = await new LocalFolderSyncRuntimeService(
            replicaB.Paths,
            replicaB.Captures).RunOnceAsync(
                replicaB.Journal.DeviceId,
                sharedFolder);

        var photo = Assert.Single(Directory.GetFiles(
            Path.Combine(sharedFolder, "Photos"),
            "*.png"));
        var link = Assert.Single(Directory.GetFiles(
            Path.Combine(sharedFolder, "Links"),
            "*.txt",
            SearchOption.AllDirectories));
        Assert.Equal(imageBytes, await File.ReadAllBytesAsync(photo));
        Assert.Contains(
            normalized.Original,
            await File.ReadAllTextAsync(link),
            StringComparison.Ordinal);
        Assert.Equal(2, result.Export.Exported);
        Assert.Equal(2, result.Publish.Uploaded);
        Assert.Equal(2, received.Cycle.Projection.Projected);
        var remoteItems = await replicaB.Captures.GetRecentAsync(10);
        Assert.Equal(2, remoteItems.Count);
        var remoteImage = Assert.Single(
            remoteItems,
            item => item.Kind == ContentKind.Image);
        Assert.Equal(
            imageBytes,
            await File.ReadAllBytesAsync(Path.Combine(
                replicaB.Paths.RootDirectory,
                remoteImage.ContentPath!)));
        Assert.Single(Directory.GetFiles(
            Path.Combine(sharedFolder, "Photos"),
            "*.png"));
    }

    [Fact]
    public async Task SourceDeletionRemovesReadableFilesAndRemoteGalleryItems()
    {
        var replicaA = await CreateReplicaAsync("delete-source-a");
        var replicaB = await CreateReplicaAsync("delete-source-b");
        var sharedFolder = Path.Combine(_root, "shared-delete-source");
        var capturedImage = await CaptureImageAsync(replicaA, 41);
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/delete-from-source",
            out var normalized));
        var capturedUrl = await replicaA.Captures.UpsertUrlAsync(
            new UrlCaptureRequest(
                Guid.NewGuid(),
                normalized.Original,
                normalized,
                SourceApp.Discord,
                CaptureMethod.DiscordConfirmedSend,
                DeliveryStatus.Confirmed,
                "delete-sync-test",
                DateTimeOffset.Parse("2026-07-26T18:31:00+09:00"),
                ["url-match"]));
        var runtimeA = new LocalFolderSyncRuntimeService(
            replicaA.Paths,
            replicaA.Captures);
        var runtimeB = new LocalFolderSyncRuntimeService(
            replicaB.Paths,
            replicaB.Captures);

        await runtimeA.RunOnceAsync(
            replicaA.Journal.DeviceId,
            sharedFolder);
        await runtimeB.RunOnceAsync(
            replicaB.Journal.DeviceId,
            sharedFolder);
        Assert.Equal(2, (await replicaB.Captures.GetRecentAsync(10)).Count);
        Assert.Single(Directory.GetFiles(
            Path.Combine(sharedFolder, "Photos")));
        Assert.Single(Directory.GetFiles(
            Path.Combine(sharedFolder, "Links"),
            "*.txt",
            SearchOption.AllDirectories));

        Assert.True(await replicaA.Captures.DeleteItemAsync(
            capturedImage.ItemId));
        Assert.True(await replicaA.Captures.DeleteItemAsync(
            capturedUrl.ItemId));
        await runtimeA.RunOnceAsync(
            replicaA.Journal.DeviceId,
            sharedFolder);
        await runtimeB.RunOnceAsync(
            replicaB.Journal.DeviceId,
            sharedFolder);
        await runtimeB.RunOnceAsync(
            replicaB.Journal.DeviceId,
            sharedFolder);

        Assert.Empty(Directory.GetFiles(
            Path.Combine(sharedFolder, "Photos")));
        Assert.Empty(Directory.GetFiles(
            Path.Combine(sharedFolder, "Links"),
            "*.txt",
            SearchOption.AllDirectories));
        Assert.Empty(await replicaA.Captures.GetRecentAsync(10));
        Assert.Empty(await replicaB.Captures.GetRecentAsync(10));
    }

    [Fact]
    public async Task DestinationDeletionDoesNotReappearAndDeletesSourceItem()
    {
        var replicaA = await CreateReplicaAsync("delete-destination-a");
        var replicaB = await CreateReplicaAsync("delete-destination-b");
        var sharedFolder = Path.Combine(
            _root,
            "shared-delete-destination");
        var sourceItem = await CaptureImageAsync(replicaA, 73);
        var existingDestinationItem = await CaptureImageAsync(replicaB, 73);
        Assert.NotEqual(sourceItem.ItemId, existingDestinationItem.ItemId);
        var runtimeA = new LocalFolderSyncRuntimeService(
            replicaA.Paths,
            replicaA.Captures);
        var runtimeB = new LocalFolderSyncRuntimeService(
            replicaB.Paths,
            replicaB.Captures);

        await runtimeA.RunOnceAsync(
            replicaA.Journal.DeviceId,
            sharedFolder);
        await runtimeB.RunOnceAsync(
            replicaB.Journal.DeviceId,
            sharedFolder);
        var destinationItem = Assert.Single(
            await replicaB.Captures.GetRecentAsync(10));
        Assert.Equal(existingDestinationItem.ItemId, destinationItem.ItemId);

        Assert.True(await replicaB.Captures.DeleteItemAsync(
            destinationItem.ItemId));
        await runtimeB.RunOnceAsync(
            replicaB.Journal.DeviceId,
            sharedFolder);
        await runtimeB.RunOnceAsync(
            replicaB.Journal.DeviceId,
            sharedFolder);
        await runtimeA.RunOnceAsync(
            replicaA.Journal.DeviceId,
            sharedFolder);

        Assert.Empty(await replicaB.Captures.GetRecentAsync(10));
        Assert.Empty(await replicaA.Captures.GetRecentAsync(10));
        Assert.Empty(Directory.GetFiles(
            Path.Combine(sharedFolder, "Photos")));
    }

    [Fact]
    public async Task AutomaticCleanupAlsoPublishesDeletion()
    {
        var replicaA = await CreateReplicaAsync("cleanup-delete-a");
        var replicaB = await CreateReplicaAsync("cleanup-delete-b");
        var sharedFolder = Path.Combine(_root, "shared-cleanup-delete");
        await CaptureImageAsync(replicaA, 92);
        var runtimeA = new LocalFolderSyncRuntimeService(
            replicaA.Paths,
            replicaA.Captures);
        var runtimeB = new LocalFolderSyncRuntimeService(
            replicaB.Paths,
            replicaB.Captures);
        await runtimeA.RunOnceAsync(
            replicaA.Journal.DeviceId,
            sharedFolder);
        await runtimeB.RunOnceAsync(
            replicaB.Journal.DeviceId,
            sharedFolder);

        var cleanup = await replicaA.Captures.CleanupAsync(olderThan: null);
        Assert.Equal(1, cleanup.Deleted.TotalItems);
        await runtimeA.RunOnceAsync(
            replicaA.Journal.DeviceId,
            sharedFolder);
        await runtimeB.RunOnceAsync(
            replicaB.Journal.DeviceId,
            sharedFolder);

        Assert.Empty(await replicaA.Captures.GetRecentAsync(10));
        Assert.Empty(await replicaB.Captures.GetRecentAsync(10));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static SyncCycleService CreateCycle(
        Replica replica,
        ISyncObjectStore objectStore) =>
        new(
            replica.Journal,
            objectStore,
            new SyncItemProjectionService(
                replica.Captures,
                objectStore));

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

    private static async Task<CaptureResult> CaptureImageAsync(
        Replica replica,
        byte marker)
    {
        byte[] imageBytes = [137, 80, 78, 71, marker, 10, 26, 10];
        var sha256 = Convert.ToHexString(SHA256.HashData(imageBytes));
        return await replica.Captures.UpsertImageAsync(
            new ImageCaptureRequest(
                Guid.NewGuid(),
                imageBytes,
                sha256,
                320,
                200,
                "image/png",
                ".png",
                SourceApp.KakaoTalk,
                CaptureMethod.KakaoCtrlVImage,
                DeliveryStatus.NotObserved,
                "delete-sync-test",
                DateTimeOffset.Parse("2026-07-26T18:30:00+09:00"),
                ["clipboard-image"],
                "delete-test.png"));
    }

    private sealed record Replica(
        SentoryDataPaths Paths,
        SqliteCaptureRepository Captures,
        SqliteSyncOperationJournal Journal);
}
