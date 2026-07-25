using System.Security.Cryptography;
using Sentory.Core.Sync;

namespace Sentory.Infrastructure.Tests;

internal sealed class InMemorySyncObjectStore : ISyncObjectStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SyncStoredObject> _objects =
        new(StringComparer.Ordinal);

    public bool IsOnline { get; set; } = true;

    public bool ReturnDuplicateListEntries { get; set; }

    public bool ReverseListOrder { get; set; }

    public int FailPutCount { get; set; }

    public Task<SyncObjectPage> ListAsync(
        string prefix,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureOnline();
        cancellationToken.ThrowIfCancellationRequested();
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        var offset = continuationToken is null
            ? 0
            : int.Parse(
                continuationToken,
                System.Globalization.CultureInfo.InvariantCulture);
        List<SyncObjectInfo> values;
        lock (_gate)
        {
            values = _objects.Values
                .Where(value => value.Key.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
                .Select(value => new SyncObjectInfo(
                    value.Key,
                    value.Content.LongLength,
                    value.Sha256))
                .OrderBy(value => value.Key, StringComparer.Ordinal)
                .ToList();
        }

        if (ReverseListOrder)
        {
            values.Reverse();
        }

        if (ReturnDuplicateListEntries)
        {
            values = values
                .SelectMany(value => new[] { value, value })
                .ToList();
        }

        var page = values.Skip(offset).Take(pageSize).ToArray();
        var nextOffset = offset + page.Length;
        return Task.FromResult(new SyncObjectPage(
            page,
            nextOffset < values.Count
                ? nextOffset.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                : null));
    }

    public Task<SyncPutResult> PutIfAbsentAsync(
        string key,
        ReadOnlyMemory<byte> content,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        EnsureOnline();
        cancellationToken.ThrowIfCancellationRequested();
        if (FailPutCount > 0)
        {
            FailPutCount--;
            throw new SyncStoreUnavailableException(
                "테스트에서 요청한 업로드 실패입니다.");
        }

        var bytes = content.ToArray();
        var actualSha256 = ComputeSha256(bytes);
        if (!string.Equals(
                sha256,
                actualSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "테스트 저장소 업로드 SHA-256이 일치하지 않습니다.");
        }

        lock (_gate)
        {
            if (_objects.TryGetValue(key, out var existing))
            {
                if (!string.Equals(
                        existing.Sha256,
                        actualSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "같은 키에 다른 동기화 내용이 이미 있습니다.");
                }

                return Task.FromResult(SyncPutResult.AlreadyExists);
            }

            _objects[key] = new SyncStoredObject(
                key,
                actualSha256,
                bytes);
        }

        return Task.FromResult(SyncPutResult.Created);
    }

    public Task<SyncStoredObject?> TryGetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        EnsureOnline();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_objects.TryGetValue(key, out var value))
            {
                return Task.FromResult<SyncStoredObject?>(null);
            }

            return Task.FromResult<SyncStoredObject?>(
                new SyncStoredObject(
                    value.Key,
                    value.Sha256,
                    value.Content.ToArray()));
        }
    }

    public Task<bool> ExistsAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        EnsureOnline();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(_objects.ContainsKey(key));
        }
    }

    public void Seed(SyncOperation operation)
    {
        var content = SyncOperationSerializer.Serialize(operation);
        var key = SyncOperationObjectKey.Create(operation);
        lock (_gate)
        {
            _objects[key] = new SyncStoredObject(
                key,
                ComputeSha256(content),
                content);
        }
    }

    public void Corrupt(string key)
    {
        lock (_gate)
        {
            var value = _objects[key];
            var content = value.Content.ToArray();
            content[^1] ^= 0xff;
            _objects[key] = value with
            {
                Content = content
            };
        }
    }

    private void EnsureOnline()
    {
        if (!IsOnline)
        {
            throw new SyncStoreUnavailableException(
                "테스트 저장소가 오프라인입니다.");
        }
    }

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
