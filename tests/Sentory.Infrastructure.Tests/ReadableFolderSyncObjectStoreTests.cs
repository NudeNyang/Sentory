using System.Security.Cryptography;
using System.Text;
using Sentory.Core;
using Sentory.Core.Sync;
using Sentory.Infrastructure.Sync;

namespace Sentory.Infrastructure.Tests;

public sealed class ReadableFolderSyncObjectStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Readable.Sync.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PhotoIsStoredOnceAsOriginalPreviewableFile()
    {
        var store = new ReadableFolderSyncObjectStore(_root);
        byte[] content = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3];
        var sha256 = Convert.ToHexString(
            SHA256.HashData(content)).ToLowerInvariant();
        var key = store.CreateImageObjectKey(sha256, ".png");

        var first = await store.PutIfAbsentAsync(
            key,
            content,
            sha256);
        var repeated = await store.PutIfAbsentAsync(
            key,
            content,
            sha256);
        var path = store.GetPhotoPathForTesting(sha256, ".png");
        var stored = await store.TryGetAsync(key);

        Assert.Equal(SyncPutResult.Created, first);
        Assert.Equal(SyncPutResult.AlreadyExists, repeated);
        Assert.EndsWith($"{sha256}.png", path, StringComparison.Ordinal);
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.Equal(content, stored!.Content);
        Assert.False(File.Exists($"{path}.sobj"));
    }

    [Fact]
    public async Task PublishingUrlOperationCreatesReadableUtf8Link()
    {
        var store = new ReadableFolderSyncObjectStore(_root);
        var capturedAt = DateTimeOffset.Parse(
            "2026-07-26T17:30:10+09:00");
        var payload = SyncItemPayload.CreateUrl(
            new SyncUrlContent(
                "https://example.com/article?q=sentory",
                "https://example.com/article?q=sentory",
                "example.com"),
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            "context",
            capturedAt,
            ["url-match"]);
        var operation = SyncOperation.Create(
            SyncDeviceIdentity.Create(),
            1,
            Guid.NewGuid(),
            SyncOperationKind.Upsert,
            capturedAt,
            SyncItemPayloadSerializer.Serialize(payload));
        var content = SyncOperationSerializer.Serialize(operation);
        var sha256 = Convert.ToHexString(
            SHA256.HashData(content)).ToLowerInvariant();

        await store.PutIfAbsentAsync(
            SyncOperationObjectKey.Create(operation),
            content,
            sha256);

        var link = Assert.Single(
            Directory.GetFiles(
                store.LinksDirectory,
                "*.txt",
                SearchOption.AllDirectories));
        var text = await File.ReadAllTextAsync(
            link,
            new UTF8Encoding(false));
        Assert.Contains(
            "주소: https://example.com/article?q=sentory",
            text,
            StringComparison.Ordinal);
        Assert.Contains("출처: Discord", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFEFF', text);
        Assert.True(await store.ExistsAsync(
            SyncOperationObjectKey.Create(operation)));
        Assert.StartsWith(
            store.InternalStoreDirectory,
            Path.GetFullPath(
                Directory.GetFiles(
                    store.InternalStoreDirectory,
                    "*.sobj",
                    SearchOption.AllDirectories).Single()),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadableImageKeyNormalizesSupportedExtensions()
    {
        var sha256 = new string('a', 64);

        var key = SyncBlobObjectKey.CreateReadable(sha256, ".JPEG");

        Assert.Equal($"photos/sha256/{sha256}.jpg", key);
        Assert.True(SyncBlobObjectKey.TryParseReadable(
            key,
            out var parsedSha256,
            out var extension));
        Assert.Equal(sha256, parsedSha256);
        Assert.Equal(".jpg", extension);
        Assert.Throws<NotSupportedException>(() =>
            SyncBlobObjectKey.CreateReadable(sha256, ".svg"));
    }

    [Fact]
    public async Task CorruptedReadablePhotoIsRejected()
    {
        var store = new ReadableFolderSyncObjectStore(_root);
        byte[] content = [137, 80, 78, 71, 13, 10, 26, 10, 1];
        var sha256 = Convert.ToHexString(
            SHA256.HashData(content)).ToLowerInvariant();
        var key = store.CreateImageObjectKey(sha256, ".png");
        await store.PutIfAbsentAsync(key, content, sha256);
        await File.WriteAllBytesAsync(
            store.GetPhotoPathForTesting(sha256, ".png"),
            [1, 2, 3]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.TryGetAsync(key));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
