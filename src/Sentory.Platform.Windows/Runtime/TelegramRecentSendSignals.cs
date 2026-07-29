namespace Sentory.Platform.Windows.Runtime;

internal sealed class TelegramRecentSendSignals
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

    public bool TryTakeApplicable(
        string contextHash,
        uint processId,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt)
    {
        RemoveExpired(observedAt);
        if (_signals.TryGetValue(contextHash, out var exact) &&
            IsApplicable(exact, pastedAt, observedAt))
        {
            RemoveAliases(exact);
            return true;
        }

        if (_processSignals.TryGetValue(processId, out var process) &&
            IsApplicable(process, pastedAt, observedAt))
        {
            RemoveAliases(process);
            return true;
        }

        return false;
    }

    public bool TryConsume(DateTimeOffset sentAt) =>
        RemoveAliases(sentAt);

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

    private static bool IsApplicable(
        DateTimeOffset sentAt,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt) =>
        sentAt >= pastedAt &&
        observedAt >= sentAt &&
        observedAt - sentAt <= Retention;

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

    private bool RemoveAliases(DateTimeOffset sentAt)
    {
        var removed = false;
        foreach (var contextHash in _signals
                     .Where(pair => pair.Value == sentAt)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            removed |= _signals.Remove(contextHash);
        }

        foreach (var processId in _processSignals
                     .Where(pair => pair.Value == sentAt)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            removed |= _processSignals.Remove(processId);
        }

        return removed;
    }
}
