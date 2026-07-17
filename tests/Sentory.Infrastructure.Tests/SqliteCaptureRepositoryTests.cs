using Sentory.Core;
using Sentory.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace Sentory.Infrastructure.Tests;

public sealed class SqliteCaptureRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DuplicateEventIsIdempotent()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var eventId = Guid.NewGuid();
        var request = CreateRequest(
            eventId,
            "https://example.com/a",
            DeliveryStatus.NotObserved);

        var first = await repository.UpsertUrlAsync(request);
        var duplicate = await repository.UpsertUrlAsync(request);

        Assert.True(first.ItemCreated);
        Assert.True(first.EventApplied);
        Assert.False(duplicate.ItemCreated);
        Assert.False(duplicate.EventApplied);
        Assert.Equal(first.ItemId, duplicate.ItemId);
        Assert.Equal(1, duplicate.CaptureCount);
        Assert.Equal(0, duplicate.ShareCount);
    }

    [Fact]
    public async Task SameUrlDifferentEventsIncrementCaptureOnlyForKakao()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();

        await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/a",
            DeliveryStatus.NotObserved));
        var second = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/a",
            DeliveryStatus.NotObserved));

        Assert.False(second.ItemCreated);
        Assert.Equal(2, second.CaptureCount);
        Assert.Equal(0, second.ShareCount);
    }

    [Fact]
    public async Task ConfirmedDeliveryIncrementsShareCount()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();

        await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/a",
            DeliveryStatus.NotObserved));
        var confirmed = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/a",
            DeliveryStatus.Confirmed));

        Assert.Equal(2, confirmed.CaptureCount);
        Assert.Equal(1, confirmed.ShareCount);

        var recent = await repository.GetRecentAsync(10);
        var item = Assert.Single(recent);
        Assert.Equal(DeliveryStatus.Confirmed, item.DeliveryStatus);
        Assert.Equal(CaptureMethod.KakaoCtrlVUrl, item.LastCaptureMethod);
    }

    [Fact]
    public async Task ImageIsStoredByHashAndDeduplicated()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        byte[] bytes = [1, 2, 3, 4, 5];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var first = await repository.UpsertImageAsync(CreateImageRequest(
            Guid.NewGuid(),
            bytes,
            hash));
        var second = await repository.UpsertImageAsync(CreateImageRequest(
            Guid.NewGuid(),
            bytes,
            hash));

        Assert.True(first.ItemCreated);
        Assert.False(second.ItemCreated);
        Assert.Equal(first.ItemId, second.ItemId);
        Assert.Equal(2, second.CaptureCount);
        Assert.Equal(0, second.ShareCount);

        var item = Assert.Single(await repository.GetRecentAsync(10));
        Assert.Equal(ContentKind.Image, item.Kind);
        Assert.Equal(hash.ToLowerInvariant(), item.Sha256);
        Assert.NotNull(item.ContentPath);
        Assert.True(File.Exists(Path.Combine(_root, item.ContentPath!)));
    }

    [Fact]
    public async Task DeleteRemovesItemEventsAndImageFile()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        byte[] bytes = [9, 8, 7, 6];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var created = await repository.UpsertImageAsync(CreateImageRequest(
            Guid.NewGuid(),
            bytes,
            hash));
        var item = Assert.Single(await repository.GetRecentAsync(10));
        var absolutePath = Path.Combine(_root, item.ContentPath!);

        var deleted = await repository.DeleteItemAsync(created.ItemId);

        Assert.True(deleted);
        Assert.Empty(await repository.GetRecentAsync(10));
        Assert.False(File.Exists(absolutePath));
        Assert.False(await repository.DeleteItemAsync(created.ItemId));
    }

    [Fact]
    public async Task FavoriteAndCopyUsageArePersisted()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var created = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/favorite",
            DeliveryStatus.NotObserved));
        var firstCopy = DateTimeOffset.UtcNow.AddMinutes(-2);
        var secondCopy = DateTimeOffset.UtcNow;

        Assert.True(await repository.SetFavoriteAsync(
            created.ItemId,
            true));
        Assert.True(await repository.RecordCopyAsync(
            created.ItemId,
            firstCopy));
        Assert.True(await repository.RecordCopyAsync(
            created.ItemId,
            secondCopy));

        var item = Assert.Single(await repository.GetRecentAsync(10));
        Assert.True(item.IsFavorite);
        Assert.Equal(2, item.CopyCount);
        Assert.Equal(secondCopy, item.LastCopiedAt);
    }

    [Fact]
    public async Task UsageUpdatesReturnFalseForMissingItem()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var missing = Guid.NewGuid();

        Assert.False(await repository.SetFavoriteAsync(missing, true));
        Assert.False(await repository.RecordCopyAsync(
            missing,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SettingsStoreReadsLegacyJsonAndPersistsWindowState()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        paths.EnsureDirectories();
        File.WriteAllText(
            paths.SettingsPath,
            """{"SortMode":"Oldest"}""");
        var store = new SentorySettingsStore(paths);

        var settings = store.Load();

        Assert.Equal("Oldest", settings.SortMode);
        Assert.False(settings.IsDarkTheme);
        settings.IsDarkTheme = true;
        settings.WindowLeft = 120;
        settings.WindowTop = 80;
        settings.WindowWidth = 1100;
        settings.WindowHeight = 720;
        settings.WindowMaximized = true;
        store.Save(settings);

        var restored = store.Load();
        Assert.True(restored.IsDarkTheme);
        Assert.Equal(120, restored.WindowLeft);
        Assert.Equal(80, restored.WindowTop);
        Assert.Equal(1100, restored.WindowWidth);
        Assert.Equal(720, restored.WindowHeight);
        Assert.True(restored.WindowMaximized);
    }

    private SqliteCaptureRepository CreateRepository() =>
        new(SentoryDataPaths.ForRoot(_root));

    private static UrlCaptureRequest CreateRequest(
        Guid eventId,
        string url,
        DeliveryStatus deliveryStatus)
    {
        Assert.True(UrlNormalizer.TryNormalize(url, out var normalized));
        return new UrlCaptureRequest(
            eventId,
            url,
            normalized,
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVUrl,
            deliveryStatus,
            "test-context",
            DateTimeOffset.UtcNow,
            ["test"]);
    }

    private static ImageCaptureRequest CreateImageRequest(
        Guid eventId,
        byte[] bytes,
        string hash) =>
        new(
            eventId,
            bytes,
            hash,
            48,
            32,
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVImage,
            DeliveryStatus.NotObserved,
            "test-context",
            DateTimeOffset.UtcNow,
            ["test"]);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
