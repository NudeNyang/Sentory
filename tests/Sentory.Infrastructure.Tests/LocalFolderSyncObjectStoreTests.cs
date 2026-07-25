using System.Security.Cryptography;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class LocalFolderSyncObjectStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Local.Sync.Store.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TwoStoreInstancesShareImmutableObject()
    {
        var writer = new LocalFolderSyncObjectStore(_root);
        var reader = new LocalFolderSyncObjectStore(_root);
        byte[] content = [1, 2, 3, 4, 5];
        var sha256 = ComputeSha256(content);
        const string key = "devices/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/" +
                           "operations/00000000000000000001-" +
                           "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json";

        var put = await writer.PutIfAbsentAsync(
            key,
            content,
            sha256);
        var page = await reader.ListAsync("devices/", null, 10);
        var stored = await reader.TryGetAsync(key);

        Assert.Equal(SyncPutResult.Created, put);
        var info = Assert.Single(page.Items);
        Assert.Equal(key, info.Key);
        Assert.Equal(content.Length, info.Size);
        Assert.Equal(sha256, info.Sha256);
        Assert.Null(page.ContinuationToken);
        Assert.NotNull(stored);
        Assert.Equal(content, stored.Content);
        Assert.Equal(sha256, stored.Sha256);
        Assert.True(await reader.ExistsAsync(key));
    }

    [Fact]
    public async Task ListingUsesStableKeyContinuationToken()
    {
        var store = new LocalFolderSyncObjectStore(_root);
        var keys = new[]
        {
            "devices/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/" +
            "operations/00000000000000000001-" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json",
            "devices/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/" +
            "operations/00000000000000000002-" +
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json",
            "devices/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/" +
            "operations/00000000000000000001-" +
            "cccccccccccccccccccccccccccccccc.json"
        };
        foreach (var (key, index) in keys.Select(
                     (key, index) => (key, index)))
        {
            var content = new byte[] { (byte)index };
            await store.PutIfAbsentAsync(
                key,
                content,
                ComputeSha256(content));
        }

        var first = await store.ListAsync(
            "devices/",
            null,
            2);
        var second = await store.ListAsync(
            "devices/",
            first.ContinuationToken,
            2);

        Assert.Equal(keys[..2], first.Items.Select(item => item.Key));
        Assert.NotNull(first.ContinuationToken);
        Assert.Equal(keys[2..], second.Items.Select(item => item.Key));
        Assert.Null(second.ContinuationToken);
    }

    [Fact]
    public async Task RepeatedPutAcceptsSameContentAndRejectsConflict()
    {
        var store = new LocalFolderSyncObjectStore(_root);
        const string key =
            "blobs/sha256/aa/" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" +
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        byte[] original = [1, 2, 3];
        byte[] conflicting = [4, 5, 6];
        await store.PutIfAbsentAsync(
            key,
            original,
            ComputeSha256(original));

        var repeated = await store.PutIfAbsentAsync(
            key,
            original,
            ComputeSha256(original));
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.PutIfAbsentAsync(
                key,
                conflicting,
                ComputeSha256(conflicting)));

        Assert.Equal(SyncPutResult.AlreadyExists, repeated);
        Assert.Contains("다른 내용", exception.Message);
    }

    [Fact]
    public async Task ConcurrentSameContentPutCreatesOneObject()
    {
        var first = new LocalFolderSyncObjectStore(_root);
        var second = new LocalFolderSyncObjectStore(_root);
        const string key =
            "blobs/sha256/cc/" +
            "cccccccccccccccccccccccccccccccc" +
            "cccccccccccccccccccccccccccccccc";
        byte[] content = [7, 8, 9];
        var sha256 = ComputeSha256(content);

        var results = await Task.WhenAll(
            first.PutIfAbsentAsync(key, content, sha256),
            second.PutIfAbsentAsync(key, content, sha256));

        Assert.Contains(SyncPutResult.Created, results);
        Assert.Contains(SyncPutResult.AlreadyExists, results);
        Assert.Equal(
            content,
            (await first.TryGetAsync(key))!.Content);
    }

    [Fact]
    public async Task PartialCloudCopyIsHiddenUntilComplete()
    {
        var store = new LocalFolderSyncObjectStore(_root);
        const string key =
            "devices/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/" +
            "operations/00000000000000000001-" +
            "dddddddddddddddddddddddddddddddd.json";
        var path = store.GetObjectPathForTesting(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(
            path,
            "SENTORY1"u8.ToArray());

        var page = await store.ListAsync("devices/", null, 10);

        Assert.Empty(page.Items);
        Assert.False(await store.ExistsAsync(key));
        await Assert.ThrowsAsync<SyncStoreUnavailableException>(() =>
            store.TryGetAsync(key));
    }

    [Fact]
    public async Task SameLengthCorruptionIsRejectedOnDownload()
    {
        var store = new LocalFolderSyncObjectStore(_root);
        const string key =
            "blobs/sha256/ee/" +
            "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee" +
            "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        byte[] content = [10, 11, 12, 13];
        await store.PutIfAbsentAsync(
            key,
            content,
            ComputeSha256(content));
        var path = store.GetObjectPathForTesting(key);
        var file = await File.ReadAllBytesAsync(path);
        file[^1] ^= 0xff;
        await File.WriteAllBytesAsync(path, file);

        Assert.Single(
            (await store.ListAsync("blobs/", null, 10)).Items);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.TryGetAsync(key));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("devices/../../outside")]
    [InlineData("devices\\outside")]
    [InlineData("/rooted")]
    [InlineData("devices/UPPERCASE")]
    public async Task UnsafeObjectKeyIsRejected(string key)
    {
        var store = new LocalFolderSyncObjectStore(_root);
        byte[] content = [1];

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.PutIfAbsentAsync(
                key,
                content,
                ComputeSha256(content)));
    }

    [Fact]
    public async Task InvalidContinuationTokenIsRejected()
    {
        var store = new LocalFolderSyncObjectStore(_root);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.ListAsync("devices/", "***", 10));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
