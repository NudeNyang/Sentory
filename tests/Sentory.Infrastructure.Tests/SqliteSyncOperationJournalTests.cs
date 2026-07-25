using System.Text;
using Microsoft.Data.Sqlite;
using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class SqliteSyncOperationJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Sync.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TwoSqliteReplicasReceiveOperationExactlyOnce()
    {
        var replicaA = await CreateReplicaAsync("a");
        var replicaB = await CreateReplicaAsync("b");
        var objectStore = new InMemorySyncObjectStore
        {
            ReturnDuplicateListEntries = true,
            ReverseListOrder = true
        };
        var itemId = Guid.NewGuid();
        var local = await replicaA.Journal.AppendLocalAsync(
            itemId,
            SyncOperationKind.Upsert,
            DateTimeOffset.Parse("2026-07-25T12:00:00+09:00"),
            Encoding.UTF8.GetBytes(
                """{"kind":"url","url":"https://example.com"}"""));

        var upload = await new SyncCoordinator(
            replicaA.Journal,
            objectStore).RunOnceAsync();
        var firstDownload = await new SyncCoordinator(
            replicaB.Journal,
            objectStore).RunOnceAsync();
        var duplicateDownload = await new SyncCoordinator(
            replicaB.Journal,
            objectStore).RunOnceAsync();
        var received = await replicaB.Journal.GetReceivedAsync();

        Assert.Equal(1, upload.Uploaded);
        Assert.Equal(1, firstDownload.Downloaded);
        Assert.Equal(0, duplicateDownload.Downloaded);
        var remote = Assert.Single(received);
        Assert.Equal(local.OperationId, remote.OperationId);
        Assert.Equal(itemId, remote.ItemId);
        Assert.Equal(local.Payload, remote.Payload);
        Assert.Equal(
            1,
            (await replicaB.Journal.GetCheckpointAsync(
                replicaA.Journal.DeviceId)).LastSequence);
    }

    [Fact]
    public async Task OfflineReplicaCatchesUpAfterRestart()
    {
        var replicaA = await CreateReplicaAsync("a");
        var replicaB = await CreateReplicaAsync("b");
        var objectStore = new InMemorySyncObjectStore();
        await replicaA.Journal.AppendLocalAsync(
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            Encoding.UTF8.GetBytes("""{"kind":"url"}"""));
        await new SyncCoordinator(
            replicaA.Journal,
            objectStore).RunOnceAsync();
        objectStore.IsOnline = false;

        await Assert.ThrowsAsync<SyncStoreUnavailableException>(() =>
            new SyncCoordinator(
                replicaB.Journal,
                objectStore).RunOnceAsync());

        objectStore.IsOnline = true;
        var restarted = new SqliteSyncOperationJournal(
            replicaB.Paths,
            replicaB.Journal.DeviceId);
        await restarted.InitializeAsync();
        var result = await new SyncCoordinator(
            restarted,
            objectStore).RunOnceAsync();

        Assert.Equal(1, result.Downloaded);
        Assert.Single(await restarted.GetReceivedAsync());
    }

    [Fact]
    public async Task FailedUploadRemainsPendingForRetry()
    {
        var replica = await CreateReplicaAsync("a");
        var objectStore = new InMemorySyncObjectStore
        {
            FailPutCount = 1
        };
        await replica.Journal.AppendLocalAsync(
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            new byte[] { 1, 2, 3 });

        await Assert.ThrowsAsync<SyncStoreUnavailableException>(() =>
            new SyncCoordinator(
                replica.Journal,
                objectStore).RunOnceAsync());
        Assert.Single(await replica.Journal.GetUnpublishedAsync(10));

        var result = await new SyncCoordinator(
            replica.Journal,
            objectStore).RunOnceAsync();

        Assert.Equal(1, result.Uploaded);
        Assert.Empty(await replica.Journal.GetUnpublishedAsync(10));
    }

    [Fact]
    public async Task MissingSequenceBlocksLaterOperationUntilGapIsFilled()
    {
        var replicaB = await CreateReplicaAsync("b");
        var deviceA = SyncDeviceIdentity.Create();
        var itemId = Guid.NewGuid();
        var operation1 = SyncOperation.Create(
            deviceA,
            1,
            itemId,
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            [1]);
        var operation2 = SyncOperation.Create(
            deviceA,
            2,
            itemId,
            SyncOperationKind.Delete,
            DateTimeOffset.UtcNow.AddSeconds(1),
            []);
        var objectStore = new InMemorySyncObjectStore();
        objectStore.Seed(operation2);

        var gapResult = await new SyncCoordinator(
            replicaB.Journal,
            objectStore).RunOnceAsync();

        Assert.Equal(1, gapResult.SequenceGaps);
        Assert.Empty(await replicaB.Journal.GetReceivedAsync());
        Assert.Equal(
            0,
            (await replicaB.Journal.GetCheckpointAsync(
                deviceA)).LastSequence);

        objectStore.Seed(operation1);
        var catchUpResult = await new SyncCoordinator(
            replicaB.Journal,
            objectStore).RunOnceAsync();

        Assert.Equal(2, catchUpResult.Downloaded);
        Assert.Equal(
            new[] { operation1.OperationId, operation2.OperationId },
            (await replicaB.Journal.GetReceivedAsync())
            .Select(operation => operation.OperationId));
        Assert.Equal(
            2,
            (await replicaB.Journal.GetCheckpointAsync(
                deviceA)).LastSequence);
    }

    [Fact]
    public async Task CorruptedRemoteObjectDoesNotAdvanceCheckpoint()
    {
        var replicaB = await CreateReplicaAsync("b");
        var operation = SyncOperation.Create(
            SyncDeviceIdentity.Create(),
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            [1, 2, 3]);
        var objectStore = new InMemorySyncObjectStore();
        objectStore.Seed(operation);
        objectStore.Corrupt(SyncOperationObjectKey.Create(operation));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SyncCoordinator(
                replicaB.Journal,
                objectStore).RunOnceAsync());

        Assert.Empty(await replicaB.Journal.GetReceivedAsync());
        Assert.Equal(
            0,
            (await replicaB.Journal.GetCheckpointAsync(
                operation.DeviceId)).LastSequence);
    }

    [Fact]
    public async Task ReusedOperationIdWithDifferentContentIsRejected()
    {
        var replicaB = await CreateReplicaAsync("b");
        var deviceA = SyncDeviceIdentity.Create();
        var operationId = Guid.NewGuid();
        var first = SyncOperation.Create(
            deviceA,
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            [1],
            operationId);
        Assert.Equal(
            SyncApplyResult.Applied,
            await replicaB.Journal.ApplyRemoteAsync(first));
        var conflicting = SyncOperation.Create(
            deviceA,
            2,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow.AddSeconds(1),
            [2],
            operationId);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            replicaB.Journal.ApplyRemoteAsync(conflicting));

        Assert.Contains("서로 다른 내용", exception.Message);
        Assert.Equal(
            1,
            (await replicaB.Journal.GetCheckpointAsync(
                deviceA)).LastSequence);
    }

    [Fact]
    public async Task ConflictingOperationsAtSameSequenceAreRejected()
    {
        var replicaB = await CreateReplicaAsync("b");
        var deviceA = SyncDeviceIdentity.Create();
        var first = SyncOperation.Create(
            deviceA,
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            [1]);
        var conflicting = SyncOperation.Create(
            deviceA,
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            [2]);
        var objectStore = new InMemorySyncObjectStore();
        objectStore.Seed(first);
        objectStore.Seed(conflicting);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SyncCoordinator(
                replicaB.Journal,
                objectStore).RunOnceAsync());

        Assert.Contains("같은 기기와 순번", exception.Message);
        Assert.Empty(await replicaB.Journal.GetReceivedAsync());
        Assert.Equal(
            0,
            (await replicaB.Journal.GetCheckpointAsync(
                deviceA)).LastSequence);
    }

    [Fact]
    public async Task ExistingRemoteObjectCompletesInterruptedPublish()
    {
        var replica = await CreateReplicaAsync("a");
        var operation = await replica.Journal.AppendLocalAsync(
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            new byte[] { 1, 2, 3 });
        var objectStore = new InMemorySyncObjectStore();
        objectStore.Seed(operation);

        var result = await new SyncCoordinator(
            replica.Journal,
            objectStore).RunOnceAsync();

        Assert.Equal(1, result.AlreadyUploaded);
        Assert.Empty(await replica.Journal.GetUnpublishedAsync(10));
    }

    [Fact]
    public async Task LocalSequenceContinuesAfterRestart()
    {
        var replica = await CreateReplicaAsync("a");
        var first = await replica.Journal.AppendLocalAsync(
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            Array.Empty<byte>());
        var restarted = new SqliteSyncOperationJournal(
            replica.Paths,
            replica.Journal.DeviceId);
        await restarted.InitializeAsync();
        var second = await restarted.AppendLocalAsync(
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            DateTimeOffset.UtcNow,
            Array.Empty<byte>());

        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
    }

    [Fact]
    public async Task ExistingDatabaseRejectsDifferentDeviceIdentity()
    {
        var replica = await CreateReplicaAsync("a");
        var mismatched = new SqliteSyncOperationJournal(
            replica.Paths,
            SyncDeviceIdentity.Create());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => mismatched.InitializeAsync());

        Assert.Contains("다른 동기화 기기 ID", exception.Message);
    }

    [Fact]
    public async Task PendingItemExportIsRecordedAtomically()
    {
        var replica = await CreateReplicaAsync("a");
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/export",
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
                DateTimeOffset.Parse("2026-07-26T11:00:00+09:00"),
                ["url-match"]));
        var objectStore = new InMemorySyncObjectStore();
        var exporter = new SyncItemExportService(
            replica.Journal,
            objectStore,
            replica.Paths);

        var first = await exporter.ExportPendingAsync(10);
        var repeated = await exporter.ExportPendingAsync(10);
        var operations = await replica.Journal.GetUnpublishedAsync(10);

        Assert.Equal(1, first.Exported);
        Assert.Equal(0, repeated.Exported);
        Assert.Single(operations);

        await replica.Captures.UpsertUrlAsync(
            new UrlCaptureRequest(
                Guid.NewGuid(),
                normalized.Original,
                normalized,
                SourceApp.Discord,
                CaptureMethod.DiscordConfirmedSend,
                DeliveryStatus.Confirmed,
                "context",
                DateTimeOffset.Parse("2026-07-26T11:01:00+09:00"),
                ["url-match"]));
        var changed = await exporter.ExportPendingAsync(10);

        Assert.Equal(1, changed.Exported);
        Assert.Equal(
            new long[] { 1, 2 },
            (await replica.Journal.GetUnpublishedAsync(10))
            .Select(operation => operation.Sequence));
    }

    [Fact]
    public async Task ChangingStoreResetsOnlySyncStateAndRequeuesItems()
    {
        var replica = await CreateReplicaAsync("a");
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/new-store",
            out var normalized));
        await replica.Captures.UpsertUrlAsync(
            new UrlCaptureRequest(
                Guid.NewGuid(),
                normalized.Original,
                normalized,
                SourceApp.KakaoTalk,
                CaptureMethod.KakaoCtrlVUrl,
                DeliveryStatus.NotObserved,
                "context",
                DateTimeOffset.UtcNow,
                ["clipboard-url"]));
        var exporter = new SyncItemExportService(
            replica.Journal,
            new InMemorySyncObjectStore(),
            replica.Paths);
        Assert.Equal(
            1,
            (await exporter.ExportPendingAsync(10)).Exported);
        var newDeviceId = SyncDeviceIdentity.Create();

        await SqliteSyncOperationJournal.ResetForNewStoreAsync(
            replica.Paths,
            newDeviceId);
        var resetJournal = new SqliteSyncOperationJournal(
            replica.Paths,
            newDeviceId);
        await resetJournal.InitializeAsync();

        Assert.Empty(await resetJournal.GetUnpublishedAsync(10));
        Assert.Single(
            await resetJournal.GetPendingItemExportsAsync(10));
        Assert.Single(await replica.Captures.GetRecentAsync(10));
        var operation = await new SyncItemExportService(
            resetJournal,
            new InMemorySyncObjectStore(),
            replica.Paths).ExportPendingAsync(10);
        Assert.Equal(1, operation.Exported);
        Assert.Equal(
            1,
            Assert.Single(
                await resetJournal.GetUnpublishedAsync(10)).Sequence);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<Replica> CreateReplicaAsync(string name)
    {
        var paths = SentoryDataPaths.ForRoot(
            Path.Combine(_root, name));
        var captureRepository = new SqliteCaptureRepository(paths);
        await captureRepository.InitializeAsync();
        var journal = new SqliteSyncOperationJournal(
            paths,
            SyncDeviceIdentity.Create());
        await journal.InitializeAsync();
        return new Replica(paths, captureRepository, journal);
    }

    private sealed record Replica(
        SentoryDataPaths Paths,
        SqliteCaptureRepository Captures,
        SqliteSyncOperationJournal Journal);
}
