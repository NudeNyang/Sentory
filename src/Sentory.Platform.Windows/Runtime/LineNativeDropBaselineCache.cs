using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal sealed class LineNativeDropBaselineCache(TimeSpan maximumAge)
{
    private readonly object _gate = new();
    private Entry? _entry;

    public void Observe(
        LineDropTarget target,
        LineAccessibilitySnapshot snapshot,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            _entry = new Entry(
                target.MainWindow,
                target.ProcessId,
                snapshot,
                capturedAt);
        }
    }

    public bool TryGet(
        LineDropTarget target,
        DateTimeOffset now,
        out LineAccessibilitySnapshot snapshot,
        out TimeSpan age)
    {
        if (!TryGetLastKnown(target, now, out snapshot, out age) ||
            age > maximumAge)
        {
            snapshot = null!;
            return false;
        }

        return true;
    }

    public bool TryGetLastKnown(
        LineDropTarget target,
        DateTimeOffset now,
        out LineAccessibilitySnapshot snapshot,
        out TimeSpan age)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            age = _entry is null
                ? TimeSpan.MaxValue
                : now - _entry.CapturedAt;
            if (_entry is null ||
                _entry.MainWindow != target.MainWindow ||
                _entry.ProcessId != target.ProcessId ||
                age < TimeSpan.Zero)
            {
                snapshot = null!;
                return false;
            }

            snapshot = _entry.Snapshot;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entry = null;
        }
    }

    private sealed record Entry(
        nint MainWindow,
        uint ProcessId,
        LineAccessibilitySnapshot Snapshot,
        DateTimeOffset CapturedAt);
}
