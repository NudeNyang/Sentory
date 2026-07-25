using Sentory.Infrastructure.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class SyncRuntimeStatusTrackerTests
{
    [Fact]
    public void StatusChangePublishesSnapshotAndKeepsLastSuccess()
    {
        var tracker = new SyncRuntimeStatusTracker();
        var changed = new List<SyncRuntimeSnapshot>();
        tracker.Changed += changed.Add;
        var succeededAt = DateTimeOffset.Parse(
            "2026-07-26T12:00:00+09:00");

        tracker.Update(
            SyncRuntimeState.Succeeded,
            succeededAt,
            succeededAt);
        tracker.Update(
            SyncRuntimeState.FolderUnavailable,
            succeededAt.AddMinutes(1));

        Assert.Equal(2, changed.Count);
        Assert.Equal(
            SyncRuntimeState.FolderUnavailable,
            tracker.Current.State);
        Assert.Equal(succeededAt, tracker.Current.LastSucceededAt);
    }
}
