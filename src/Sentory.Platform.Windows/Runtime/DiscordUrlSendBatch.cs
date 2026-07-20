using Sentory.Core;

namespace Sentory.Platform.Windows.Runtime;

internal sealed class DiscordUrlSendBatch(
    Guid leaderEventId,
    string contextHash,
    DateTimeOffset sentAt,
    IReadOnlyList<NormalizedUrl> initialUrls)
{
    private readonly object _gate = new();
    private readonly List<NormalizedUrl> _urls = [.. initialUrls];
    private readonly HashSet<string> _urlValues = new(
        initialUrls.Select(url => url.Value),
        StringComparer.Ordinal);

    public string ContextHash { get; } = contextHash;

    public DateTimeOffset SentAt { get; } = sentAt;

    public bool IsLeader(Guid eventId) => eventId == leaderEventId;

    public void Add(IReadOnlyList<NormalizedUrl> urls)
    {
        lock (_gate)
        {
            foreach (var url in urls)
            {
                if (_urlValues.Add(url.Value))
                {
                    _urls.Add(url);
                }
            }
        }
    }

    public IReadOnlyList<NormalizedUrl> SnapshotUrls()
    {
        lock (_gate)
        {
            return _urls.ToArray();
        }
    }
}
