namespace Sentory.Platform.Windows.Runtime;

internal sealed class DiscordAttachmentReplayGuard
{
    private const int DefaultCapacity = 512;

    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Queue<string> _order = [];
    private readonly HashSet<string> _knownIdentities =
        new(StringComparer.Ordinal);

    public DiscordAttachmentReplayGuard(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public IReadOnlyList<string> SelectUnseen(IEnumerable<string> urls)
    {
        lock (_gate)
        {
            return DiscordAttachmentUrlExtractor.SelectNewAgainstIdentities(
                urls,
                _knownIdentities);
        }
    }

    public void Record(IEnumerable<string> urls)
    {
        lock (_gate)
        {
            foreach (var url in urls)
            {
                var identity = DiscordAttachmentUrlExtractor.CreateIdentity(url);
                if (identity is null || !_knownIdentities.Add(identity))
                {
                    continue;
                }

                _order.Enqueue(identity);
                while (_order.Count > _capacity)
                {
                    _knownIdentities.Remove(_order.Dequeue());
                }
            }
        }
    }
}
