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

    private sealed record Replica(
        SentoryDataPaths Paths,
        SqliteCaptureRepository Captures,
        SqliteSyncOperationJournal Journal);
}
