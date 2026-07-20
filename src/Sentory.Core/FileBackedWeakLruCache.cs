namespace Sentory.Core;

/// <summary>
/// Reuses values created from unchanged files without keeping those values alive
/// after their callers release them.
/// </summary>
public sealed class FileBackedWeakLruCache<T>
    where T : class
{
    private readonly int _capacity;
    private readonly Dictionary<string, CacheEntry> _entries;
    private readonly LinkedList<string> _lru = [];
    private readonly object _sync = new();

    public FileBackedWeakLruCache(
        int capacity,
        StringComparer? pathComparer = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Capacity must be greater than zero.");
        }

        _capacity = capacity;
        _entries = new Dictionary<string, CacheEntry>(
            pathComparer ?? GetDefaultPathComparer());
    }

    public T? GetOrAdd(string path, Func<string, T?> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(factory);

        var fullPath = Path.GetFullPath(path);
        var fingerprint = ReadFingerprint(fullPath);
        if (fingerprint is null)
        {
            Remove(fullPath);
            return null;
        }

        lock (_sync)
        {
            if (_entries.TryGetValue(fullPath, out var cached) &&
                cached.Fingerprint == fingerprint &&
                cached.Value.TryGetTarget(out var value))
            {
                Touch(cached);
                return value;
            }

            RemoveCore(fullPath);
            var loaded = factory(fullPath);
            if (loaded is null)
            {
                return null;
            }

            var node = _lru.AddFirst(fullPath);
            _entries[fullPath] = new CacheEntry(
                fingerprint.Value,
                new WeakReference<T>(loaded),
                node);
            TrimToCapacity();
            return loaded;
        }
    }

    private void Remove(string fullPath)
    {
        lock (_sync)
        {
            RemoveCore(fullPath);
        }
    }

    private void RemoveCore(string fullPath)
    {
        if (!_entries.Remove(fullPath, out var cached))
        {
            return;
        }

        _lru.Remove(cached.Node);
    }

    private void Touch(CacheEntry cached)
    {
        _lru.Remove(cached.Node);
        _lru.AddFirst(cached.Node);
    }

    private void TrimToCapacity()
    {
        while (_entries.Count > _capacity && _lru.Last is { } oldest)
        {
            _entries.Remove(oldest.Value);
            _lru.RemoveLast();
        }
    }

    private static FileFingerprint? ReadFingerprint(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists
                ? new FileFingerprint(
                    file.Length,
                    file.LastWriteTimeUtc.Ticks)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static StringComparer GetDefaultPathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly record struct FileFingerprint(
        long Length,
        long LastWriteTimeUtcTicks);

    private sealed record CacheEntry(
        FileFingerprint Fingerprint,
        WeakReference<T> Value,
        LinkedListNode<string> Node);
}
