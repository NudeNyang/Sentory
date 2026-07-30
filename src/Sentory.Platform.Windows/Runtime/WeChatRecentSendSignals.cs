namespace Sentory.Platform.Windows.Runtime;

internal readonly record struct WeChatRecentSendSignal(
    DateTimeOffset SentAt);

internal sealed class WeChatRecentSendSignals
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, WeChatRecentSendSignal> _signals =
        new(StringComparer.Ordinal);
    private readonly Dictionary<uint, WeChatRecentSendSignal> _processSignals = [];

    public void Observe(
        string contextHash,
        DateTimeOffset sentAt) =>
        _signals[contextHash] = new WeChatRecentSendSignal(sentAt);

    public void ObserveProcess(
        uint processId,
        DateTimeOffset sentAt) =>
        _processSignals[processId] = new WeChatRecentSendSignal(sentAt);

    public bool CanApply(
        string contextHash,
        uint processId,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt)
    {
        RemoveExpired(observedAt);
        if (_signals.TryGetValue(contextHash, out var exact) &&
            IsApplicable(exact, pastedAt, observedAt))
        {
            return true;
        }

        return _processSignals.TryGetValue(processId, out var process) &&
               IsApplicable(process, pastedAt, observedAt);
    }

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
            RemoveAliases(exact.SentAt);
            return true;
        }

        if (_processSignals.TryGetValue(processId, out var process) &&
            IsApplicable(process, pastedAt, observedAt))
        {
            RemoveAliases(process.SentAt);
            return true;
        }

        return false;
    }

    public bool TryConsume(DateTimeOffset sentAt) =>
        RemoveAliases(sentAt);

    private static bool IsApplicable(
        WeChatRecentSendSignal signal,
        DateTimeOffset pastedAt,
        DateTimeOffset observedAt) =>
        signal.SentAt >= pastedAt &&
        observedAt >= signal.SentAt &&
        observedAt - signal.SentAt <= Retention;

    private void RemoveExpired(DateTimeOffset observedAt)
    {
        foreach (var contextHash in _signals
                     .Where(pair =>
                         observedAt >= pair.Value.SentAt &&
                         observedAt - pair.Value.SentAt > Retention)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _signals.Remove(contextHash);
        }

        foreach (var processId in _processSignals
                     .Where(pair =>
                         observedAt >= pair.Value.SentAt &&
                         observedAt - pair.Value.SentAt > Retention)
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
                     .Where(pair => pair.Value.SentAt == sentAt)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            removed |= _signals.Remove(contextHash);
        }

        foreach (var processId in _processSignals
                     .Where(pair => pair.Value.SentAt == sentAt)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            removed |= _processSignals.Remove(processId);
        }

        return removed;
    }
}
