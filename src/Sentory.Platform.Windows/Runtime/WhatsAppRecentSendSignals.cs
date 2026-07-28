namespace Sentory.Platform.Windows.Runtime;

internal sealed class WhatsAppRecentSendSignals
{
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);
    private readonly Dictionary<string, DateTimeOffset> _signals =
        new(StringComparer.Ordinal);

    public void Observe(string contextHash, DateTimeOffset sentAt) =>
        _signals[contextHash] = sentAt;

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
    }
}
