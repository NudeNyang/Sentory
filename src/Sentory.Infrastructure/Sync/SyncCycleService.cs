using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Sync;

public sealed record SyncCycleResult(
    SyncRunResult Transfer,
    SyncItemProjectionResult Projection);

public sealed class SyncCycleService(
    ISyncOperationJournal journal,
    ISyncObjectStore objectStore,
    SyncItemProjectionService projectionService)
{
    public async Task<SyncCycleResult> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        var transfer = await new SyncCoordinator(
            journal,
            objectStore).RunOnceAsync(cancellationToken);
        var projection = await projectionService.ProjectReceivedAsync(
            journal,
            cancellationToken);
        return new SyncCycleResult(transfer, projection);
    }
}
