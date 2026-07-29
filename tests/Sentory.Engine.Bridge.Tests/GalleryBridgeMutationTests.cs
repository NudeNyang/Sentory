using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.Engine.Bridge.Tests;

public sealed class GalleryBridgeMutationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sentory-bridge-mutation-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ItemMutationsValidateIdAndPersistThroughRepository()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/item",
            out var normalized));
        var captured = await repository.UpsertUrlAsync(new UrlCaptureRequest(
            Guid.NewGuid(),
            "https://example.com/item",
            normalized,
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            "bridge-test",
            DateTimeOffset.Now,
            ["test"]));
        var service = new GalleryBridgeService(repository, paths);
        var id = captured.ItemId.ToString("N");

        var detail = await service.GetItemAsync(id);
        Assert.NotNull(detail);
        Assert.Equal("https://example.com/item", detail.Card.OriginalUrl);

        var favorite = await service.SetFavoriteAsync(id, true);
        Assert.True(favorite.Success);
        Assert.True((await repository.GetGalleryItemAsync(captured.ItemId))!.IsFavorite);

        var copied = await service.RecordCopyAsync(id);
        Assert.True(copied.Success);
        Assert.Equal(1, copied.CopyCount);

        var deleted = await service.DeleteItemsAsync([id]);
        Assert.True(deleted.Success);
        Assert.Equal(1, deleted.Changed);
        Assert.Null(await service.GetItemAsync(id));
    }

    [Fact]
    public async Task RepeatedCopyAutomaticallyFavoritesSinglePhotoOrLink()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/repeated",
            out var normalized));
        var captured = await repository.UpsertUrlAsync(new UrlCaptureRequest(
            Guid.NewGuid(),
            "https://example.com/repeated",
            normalized,
            SourceApp.Line,
            CaptureMethod.LineConfirmedSend,
            DeliveryStatus.NotObserved,
            "bridge-test",
            DateTimeOffset.Now,
            ["test"]));
        var settingsStore = new SentorySettingsStore(paths);
        var settings = settingsStore.Load();
        settings.AutoFavoriteEnabled = true;
        settings.AutoFavoriteCopyThreshold = 2;
        settings.AutoFavoriteChangedAt = DateTimeOffset.UtcNow;
        settingsStore.Save(settings);
        var service = new GalleryBridgeService(repository, paths);
        var id = captured.ItemId.ToString("N");

        var first = await service.RecordCopyAsync(id);
        var second = await service.RecordCopyAsync(id);

        Assert.False(first.IsFavorite);
        Assert.True(second.IsFavorite);
        Assert.True((await repository.GetGalleryItemAsync(captured.ItemId))!.IsFavorite);
    }

    [Fact]
    public async Task RepeatedCopyAutomaticallyFavoritesCollection()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        CollectionMemberCaptureRequest[] members =
        [
            new(
                ContentKind.Url,
                "https://example.com/collection/first",
                "https://example.com/collection/first",
                "example.com",
                ReadOnlyMemory<byte>.Empty,
                null,
                0,
                0,
                null,
                null),
            new(
                ContentKind.Url,
                "https://example.org/collection/second",
                "https://example.org/collection/second",
                "example.org",
                ReadOnlyMemory<byte>.Empty,
                null,
                0,
                0,
                null,
                null)
        ];
        var captured = await repository.UpsertCollectionAsync(new CollectionCaptureRequest(
            Guid.NewGuid(),
            CaptureCollectionIdentity.CreateSignature(members),
            members,
            SourceApp.Line,
            CaptureMethod.LineConfirmedSend,
            DeliveryStatus.NotObserved,
            "bridge-collection-test",
            DateTimeOffset.Now,
            ["test"]));
        var settingsStore = new SentorySettingsStore(paths);
        var settings = settingsStore.Load();
        settings.AutoFavoriteEnabled = true;
        settings.AutoFavoriteCopyThreshold = 2;
        settings.AutoFavoriteChangedAt = DateTimeOffset.UtcNow;
        settingsStore.Save(settings);
        var service = new GalleryBridgeService(repository, paths);
        var id = captured.ItemId.ToString("N");

        var first = await service.RecordCopyAsync(id);
        var second = await service.RecordCopyAsync(id);

        Assert.False(first.IsFavorite);
        Assert.True(second.IsFavorite);
        Assert.True((await repository.GetGalleryItemAsync(captured.ItemId))!.IsFavorite);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
