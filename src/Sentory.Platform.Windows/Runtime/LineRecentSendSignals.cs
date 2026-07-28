namespace Sentory.Platform.Windows.Runtime;

internal sealed class LineRecentSendSignals
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, DateTimeOffset> _signals =
        new(StringComparer.Ordinal);
    private readonly Dictionary<uint, DateTimeOffset> _processSignals = [];

    public void Observe(string contextHash, DateTimeOffset sentAt) =>
        _signals[contextHash] = sentAt;

    public void ObserveProcess(uint processId, DateTimeOffset sentAt) =>
        _processSignals[processId] = sentAt;

    public bool CanApply(
        string contextHash,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt)
    {
        RemoveExpired(observedAt);
        return _signals.TryGetValue(contextHash, out var sentAt) &&
               sentAt >= pastedAt &&
               observedAt >= sentAt &&
               observedAt - sentAt <= Retention;
    }

    public bool CanApply(
        string contextHash,
        uint processId,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt) =>
        CanApply(contextHash, pastedAt, observedAt) ||
        CanApplyProcess(processId, pastedAt, observedAt);

    private bool CanApplyProcess(
        uint processId,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt)
    {
        RemoveExpired(observedAt);
        return _processSignals.TryGetValue(processId, out var sentAt) &&
               sentAt >= pastedAt &&
               observedAt >= sentAt &&
               observedAt - sentAt <= Retention;
    }

    private void RemoveExpired(DateTimeOffset observedAt)
    {
        foreach (var contextHash in _signals
                     .Where(pair =>
                         observedAt >= pair.Value &&
                         observedAt - pair.Value > Retention)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _signals.Remove(contextHash);
        }

        foreach (var processId in _processSignals
                     .Where(pair =>
                         observedAt >= pair.Value &&
                         observedAt - pair.Value > Retention)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _processSignals.Remove(processId);
        }
    }
}
