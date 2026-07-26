using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Sync;

public sealed record LocalFolderSyncRunResult(
    SyncItemExportBatchResult Export,
    SyncCycleResult Cycle,
    SyncRunResult Publish);

public sealed class LocalFolderSyncRuntimeService(
    SentoryDataPaths paths,
    ICaptureRepository captureRepository)
{
    private const int ExportBatchSize = 200;

    public async Task<LocalFolderSyncRunResult> RunOnceAsync(
        string deviceId,
        string selectedDirectory,
        CancellationToken cancellationToken = default) =>
        await RunOnceAsync(
            deviceId,
            new ReadableFolderSyncObjectStore(selectedDirectory),
            cancellationToken);

    public async Task<LocalFolderSyncRunResult> RunLegacyOnceAsync(
        string deviceId,
        string selectedDirectory,
        CancellationToken cancellationToken = default) =>
        await RunOnceAsync(
            deviceId,
            new LocalFolderSyncObjectStore(selectedDirectory),
            cancellationToken);

    private async Task<LocalFolderSyncRunResult> RunOnceAsync(
        string deviceId,
        ISyncObjectStore objectStore,
        CancellationToken cancellationToken)
    {
        var journal = new SqliteSyncOperationJournal(
            paths,
            deviceId);
        await journal.InitializeAsync(cancellationToken);
        var exporter = new SyncItemExportService(
            journal,
            objectStore,
            paths);
        var cycle = await new SyncCycleService(
            journal,
            objectStore,
            new SyncItemProjectionService(
                captureRepository,
                objectStore))
            .RunOnceAsync(cancellationToken);
        var export = await exporter.ExportPendingAsync(
            ExportBatchSize,
            cancellationToken);
        var publish = await new SyncCoordinator(
            journal,
            objectStore).RunOnceAsync(cancellationToken);
        return new LocalFolderSyncRunResult(
            export,
            cycle,
            publish);
    }
}

public enum SyncRuntimeState
{
    Disabled,
    Waiting,
    Migrating,
    Syncing,
    Succeeded,
    FolderUnavailable,
    InvalidData,
    Failed
}

public sealed record SyncRuntimeSnapshot(
    SyncRuntimeState State,
    DateTimeOffset ChangedAt,
    DateTimeOffset? LastSucceededAt = null);

public sealed class SyncRuntimeStatusTracker
{
    private readonly object _gate = new();
    private SyncRuntimeSnapshot _current = new(
        SyncRuntimeState.Disabled,
        DateTimeOffset.UtcNow);

    public event Action<SyncRuntimeSnapshot>? Changed;

    public SyncRuntimeSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Update(
        SyncRuntimeState state,
        DateTimeOffset changedAt,
        DateTimeOffset? lastSucceededAt = null)
    {
        SyncRuntimeSnapshot snapshot;
        lock (_gate)
        {
            var succeededAt = lastSucceededAt ??
                              (state == SyncRuntimeState.Succeeded
                                  ? changedAt
                                  : _current.LastSucceededAt);
            snapshot = new SyncRuntimeSnapshot(
                state,
                changedAt,
                succeededAt);
            _current = snapshot;
        }

        Changed?.Invoke(snapshot);
    }
}
