using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Runtime;

internal sealed class DiscordImageSendBatch(
    Guid leaderEventId,
    string contextHash,
    DateTimeOffset sentAt,
    IReadOnlyList<NormalizedUrl> initialUrls,
    IReadOnlyList<ClipboardImageSnapshot> initialImages)
{
    private readonly object _gate = new();
    private readonly List<NormalizedUrl> _urls = [.. initialUrls];
    private readonly HashSet<string> _urlValues = new(
        initialUrls.Select(url => url.Value),
        StringComparer.Ordinal);
    private readonly List<ClipboardImageSnapshot> _images = [.. initialImages];
    private readonly HashSet<string> _imageHashes = new(
        initialImages.Select(image => image.Sha256),
        StringComparer.OrdinalIgnoreCase);

    public string ContextHash { get; } = contextHash;

    public DateTimeOffset SentAt { get; } = sentAt;

    public bool IsLeader(Guid eventId) => eventId == leaderEventId;

    public void Add(
        IReadOnlyList<NormalizedUrl> urls,
        IReadOnlyList<ClipboardImageSnapshot> images)
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

            foreach (var image in images)
            {
                if (_imageHashes.Add(image.Sha256))
                {
                    _images.Add(image);
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

    public IReadOnlyList<ClipboardImageSnapshot> SnapshotImages()
    {
        lock (_gate)
        {
            return _images.ToArray();
        }
    }
}
