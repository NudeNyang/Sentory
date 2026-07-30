using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Sync;

public sealed record LocalFolderSyncRunResult(
    SyncItemExportBatchResult Export,
    SyncMetadataRunResult Metadata,
    SyncCycleResult Cycle,
    SyncRunResult Publish,
    bool DeviceBindingReset = false,
    bool StoreReset = false,
    SyncPublishedAssetRepairResult? PublishedAssetRepair = null)
{
    public SyncPublishedAssetRepairResult AssetRepair =>
        PublishedAssetRepair ?? new SyncPublishedAssetRepairResult(0, 0);
}

public sealed class LocalFolderSyncRuntimeService(
    SentoryDataPaths paths,
    ICaptureRepository captureRepository,
    SentorySettingsStore? settingsStore = null,
    Action? storeRecoveryStarted = null)
{
    private const int ExportBatchSize = 200;
    private readonly object _readableStoreGate = new();
    private ReadableFolderSyncObjectStore? _readableStore;
    private string? _readableStoreDirectory;
    private int _readableStoreCreationCount;

    internal int ReadableStoreCreationCount =>
        Volatile.Read(ref _readableStoreCreationCount);

    public async Task<LocalFolderSyncRunResult> RunOnceAsync(
        string deviceId,
        string selectedDirectory,
        CancellationToken cancellationToken = default)
    {
        var storeReset = false;
        if (settingsStore is not null)
        {
            var store = await new SyncStoreIdentityService(
                paths,
                settingsStore).PrepareAsync(
                    deviceId,
                    selectedDirectory,
                    cancellationToken);
            storeReset = store.StoreReset;
            deviceId = store.DeviceId;
            if (storeReset)
            {
                storeRecoveryStarted?.Invoke();
            }
        }

        return await RunOnceAsync(
            deviceId,
            GetReadableStore(selectedDirectory, storeReset),
            cancellationToken,
            storeReset);
    }

    public async Task<LocalFolderSyncRunResult> RunLegacyOnceAsync(
        string deviceId,
        string selectedDirectory,
        CancellationToken cancellationToken = default) =>
        await RunOnceAsync(
            deviceId,
            new LocalFolderSyncObjectStore(selectedDirectory),
            cancellationToken,
            storeReset: false);

    public async Task<LocalFolderSyncRunResult> RunObjectStoreOnceAsync(
        string deviceId,
        ISyncObjectStore objectStore,
        bool storeReset = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentNullException.ThrowIfNull(objectStore);
        return await RunOnceAsync(
            deviceId,
            objectStore,
            cancellationToken,
            storeReset);
    }

    private ReadableFolderSyncObjectStore GetReadableStore(
        string selectedDirectory,
        bool forceRefresh)
    {
        var fullDirectory = Path.GetFullPath(selectedDirectory);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        lock (_readableStoreGate)
        {
            if (!forceRefresh &&
                _readableStore is not null &&
                string.Equals(
                    _readableStoreDirectory,
                    fullDirectory,
                    pathComparison))
            {
                return _readableStore;
            }

            _readableStore = new ReadableFolderSyncObjectStore(
                fullDirectory);
            _readableStoreDirectory = fullDirectory;
            Interlocked.Increment(ref _readableStoreCreationCount);
            return _readableStore;
        }
    }

    private async Task<LocalFolderSyncRunResult> RunOnceAsync(
        string deviceId,
        ISyncObjectStore objectStore,
        CancellationToken cancellationToken,
        bool storeReset)
    {
        var deviceBindingReset = false;
        var journal = new SqliteSyncOperationJournal(
            paths,
            deviceId);
        try
        {
            await journal.InitializeAsync(cancellationToken);
        }
        catch (SyncDeviceBindingMismatchException)
        {
            await SqliteSyncOperationJournal.ResetForDeviceBindingChangeAsync(
                paths,
                deviceId,
                cancellationToken);
            journal = new SqliteSyncOperationJournal(
                paths,
                deviceId);
            await journal.InitializeAsync(cancellationToken);
            deviceBindingReset = true;
        }
        var metadataService = captureRepository is
            SqliteCaptureRepository sqliteRepository
            ? new SyncMetadataService(
                paths,
                journal,
                sqliteRepository,
                settingsStore)
            : null;
        var metadataExported = metadataService is null
            ? 0
            : await metadataService.CaptureLocalChangesAsync(
                cancellationToken);
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
        var metadata = metadataService is null
            ? new SyncMetadataRunResult(0, 0, 0, 0, false)
            : await metadataService.ProjectReceivedAsync(
                cancellationToken) with
            {
                Exported = metadataExported
            };
        var assetRepair = await exporter.RepairPublishedImageBlobsAsync(
            ExportBatchSize,
            cancellationToken);
        var export = await exporter.ExportPendingAsync(
            ExportBatchSize,
            cancellationToken);
        var publish = await new SyncCoordinator(
            journal,
            objectStore).RunOnceAsync(cancellationToken);
        return new LocalFolderSyncRunResult(
            export,
            metadata,
            cycle,
            publish,
            deviceBindingReset,
            storeReset,
            assetRepair);
    }
}

public enum SyncRuntimeState
{
    Disabled,
    Waiting,
    Migrating,
    Recovering,
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
