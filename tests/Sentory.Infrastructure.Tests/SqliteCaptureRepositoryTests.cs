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
    public async Task DiscordConfirmedUrlStoresSourceAndShareCount()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/discord",
            out var normalized));

        var result = await repository.UpsertUrlAsync(new UrlCaptureRequest(
            Guid.NewGuid(),
            normalized.Original,
            normalized,
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            "discord-context",
            DateTimeOffset.UtcNow,
            ["newest-message-url-match"]));
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal(1, result.ShareCount);
        Assert.Equal(SourceApp.Discord, item.LastSourceApp);
        Assert.Equal(
            CaptureMethod.DiscordConfirmedSend,
            item.LastCaptureMethod);
        Assert.Equal(DeliveryStatus.Confirmed, item.DeliveryStatus);
    }

    [Fact]
    public async Task DiscordConfirmedImageStoresSourceAndShareCount()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        byte[] bytes = [5, 4, 3, 2, 1];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var result = await repository.UpsertImageAsync(
            new ImageCaptureRequest(
                Guid.NewGuid(),
                bytes,
                hash,
                48,
                32,
                SourceApp.Discord,
                CaptureMethod.DiscordConfirmedImage,
                DeliveryStatus.Confirmed,
                "discord-context",
                DateTimeOffset.UtcNow,
                ["newest-message-image-attachment"]));
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal(1, result.ShareCount);
        Assert.Equal(ContentKind.Image, item.Kind);
        Assert.Equal(SourceApp.Discord, item.LastSourceApp);
        Assert.Equal(
            CaptureMethod.DiscordConfirmedImage,
            item.LastCaptureMethod);
        Assert.Equal(DeliveryStatus.Confirmed, item.DeliveryStatus);
    }

    [Fact]
    public async Task RecentItemIncludesEveryMessengerFromCaptureHistory()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/shared",
            DeliveryStatus.NotObserved));
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/shared",
            out var normalized));
        await repository.UpsertUrlAsync(new UrlCaptureRequest(
            Guid.NewGuid(),
            normalized.Original,
            normalized,
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            "discord-context",
            DateTimeOffset.UtcNow,
            ["newest-message-url-match"]));

        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal(
            new[] { SourceApp.Discord, SourceApp.KakaoTalk }.Order(),
            item.SourceApps!.Order());
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
    public async Task ImageLargerThanEightMegabytesIsStoredWithoutTruncation()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var bytes = RandomNumberGenerator.GetBytes(9 * 1024 * 1024);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var result = await repository.UpsertImageAsync(CreateImageRequest(
            Guid.NewGuid(),
            bytes,
            hash));

        var item = Assert.Single(await repository.GetRecentAsync(10));
        var storedPath = Path.Combine(_root, item.ContentPath!);
        Assert.True(result.EventApplied);
        Assert.Equal(bytes.Length, new FileInfo(storedPath).Length);
        Assert.Equal(hash, Convert.ToHexString(
            SHA256.HashData(await File.ReadAllBytesAsync(storedPath))));
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
    public async Task BulkDeleteRemovesDistinctItemsAndReportsMissingIds()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var first = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/delete-first",
            DeliveryStatus.NotObserved));
        var second = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/delete-second",
            DeliveryStatus.NotObserved));
        var missing = Guid.NewGuid();

        var result = await repository.DeleteItemsAsync(
            [first.ItemId, first.ItemId, missing]);
        var remaining = await repository.GetRecentAsync(10);

        Assert.Equal(2, result.RequestedItems);
        Assert.Equal(1, result.DeletedItems);
        Assert.Equal(1, result.MissingItems);
        Assert.DoesNotContain(remaining, item => item.ItemId == first.ItemId);
        Assert.Contains(remaining, item => item.ItemId == second.ItemId);
    }

    [Fact]
    public async Task InitializeSetsCurrentSchemaVersion()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);

        await repository.InitializeAsync();
        await using var connection = new SqliteConnection(
            $"Data Source={paths.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task LinkPreviewCandidatesAndMetadataArePersisted()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var first = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/preview",
            DeliveryStatus.NotObserved));
        await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.org/pending",
            DeliveryStatus.NotObserved));
        var fetchedAt = DateTimeOffset.UtcNow;

        Assert.True(await repository.UpdateLinkPreviewAsync(
            first.ItemId,
            new LinkPreviewUpdate(
                LinkPreviewStatus.Available,
                "Example title",
                "Example description",
                "link-previews/example-icon.png",
                "link-previews/example-cover.jpg",
                fetchedAt)));

        var recent = (await repository.GetRecentAsync(10))
            .Single(item => item.ItemId == first.ItemId);
        var candidates = await repository.GetLinkPreviewCandidatesAsync(
            10,
            fetchedAt.AddHours(-1));

        Assert.Equal("Example title", recent.PageTitle);
        Assert.Equal("Example description", recent.PageDescription);
        Assert.Equal("link-previews/example-icon.png", recent.SiteIconPath);
        Assert.Equal("link-previews/example-cover.jpg", recent.PreviewImagePath);
        Assert.Equal(LinkPreviewStatus.Available, recent.PreviewStatus);
        Assert.Equal(fetchedAt, recent.PreviewFetchedAt);
        Assert.Single(candidates);
        Assert.Equal("https://example.org/pending", candidates[0].Url);
    }

    [Fact]
    public async Task RepairRemovesOnlyUnreferencedAndTemporaryImageFiles()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        byte[] bytes = [4, 3, 2, 1];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await repository.UpsertImageAsync(CreateImageRequest(
            Guid.NewGuid(),
            bytes,
            hash));
        var referenced = Assert.Single(await repository.GetRecentAsync(10));
        var referencedPath = Path.Combine(_root, referenced.ContentPath!);
        var orphanPath = Path.Combine(paths.ImagesDirectory, "orphan.png");
        var temporaryPath = Path.Combine(paths.ImagesDirectory, "write.tmp");
        var unrelatedPath = Path.Combine(paths.ImagesDirectory, "notes.txt");
        var orphanPreviewPath = Path.Combine(
            paths.LinkPreviewsDirectory,
            "orphan.jpg");
        var temporaryPreviewPath = Path.Combine(
            paths.LinkPreviewsDirectory,
            "download.tmp");
        await File.WriteAllBytesAsync(orphanPath, [9]);
        await File.WriteAllBytesAsync(temporaryPath, [8]);
        await File.WriteAllTextAsync(unrelatedPath, "keep");
        await File.WriteAllBytesAsync(orphanPreviewPath, [7]);
        await File.WriteAllBytesAsync(temporaryPreviewPath, [6]);

        var result = await repository.RepairStorageAsync();

        Assert.Equal(2, result.OrphanFilesDeleted);
        Assert.Equal(2, result.TemporaryFilesDeleted);
        Assert.Equal(0, result.MissingImageFiles);
        Assert.Equal(0, result.FileDeleteFailures);
        Assert.True(File.Exists(referencedPath));
        Assert.False(File.Exists(orphanPath));
        Assert.False(File.Exists(temporaryPath));
        Assert.True(File.Exists(unrelatedPath));
        Assert.False(File.Exists(orphanPreviewPath));
        Assert.False(File.Exists(temporaryPreviewPath));
    }

    [Fact]
    public async Task RepairReportsMissingReferencedImageWithoutDeletingRecord()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        byte[] bytes = [7, 7, 7];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await repository.UpsertImageAsync(CreateImageRequest(
            Guid.NewGuid(),
            bytes,
            hash));
        var item = Assert.Single(await repository.GetRecentAsync(10));
        File.Delete(Path.Combine(_root, item.ContentPath!));

        var result = await repository.RepairStorageAsync();

        Assert.Equal(1, result.MissingImageFiles);
        Assert.Single(await repository.GetRecentAsync(10));
    }

    [Fact]
    public async Task StatisticsIncludeFavoritesKindsAndImageBytes()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var url = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/stats",
            DeliveryStatus.NotObserved));
        byte[] bytes = [2, 4, 6, 8];
        await repository.UpsertImageAsync(CreateImageRequest(
            Guid.NewGuid(),
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes))));
        await repository.SetFavoriteAsync(url.ItemId, true);

        var statistics = await repository.GetDataStatisticsAsync();

        Assert.Equal(2, statistics.TotalItems);
        Assert.Equal(1, statistics.FavoriteItems);
        Assert.Equal(1, statistics.UrlItems);
        Assert.Equal(1, statistics.ImageItems);
        Assert.Equal(bytes.Length, statistics.ImageBytes);
    }

    [Fact]
    public async Task AgeCleanupAlwaysPreservesFavoritesAndRecentItems()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var oldRegular = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/old",
            DeliveryStatus.NotObserved,
            now.AddDays(-100)));
        var oldFavorite = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/favorite-old",
            DeliveryStatus.NotObserved,
            now.AddDays(-100)));
        await repository.SetFavoriteAsync(oldFavorite.ItemId, true);
        await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/recent",
            DeliveryStatus.NotObserved,
            now.AddDays(-10)));

        var preview = await repository.PreviewCleanupAsync(now.AddDays(-90));
        var result = await repository.CleanupAsync(now.AddDays(-90));
        var remaining = await repository.GetRecentAsync(10);

        Assert.Equal(1, preview.TotalItems);
        Assert.Equal(1, result.Deleted.TotalItems);
        Assert.DoesNotContain(remaining, item => item.ItemId == oldRegular.ItemId);
        Assert.Contains(remaining, item => item.ItemId == oldFavorite.ItemId);
        Assert.Equal(2, remaining.Count);
    }

    [Fact]
    public async Task CleanupWithoutCutoffDeletesAllNonFavoritesAndImageFile()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var favorite = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/keep",
            DeliveryStatus.NotObserved));
        await repository.SetFavoriteAsync(favorite.ItemId, true);
        byte[] bytes = [1, 3, 5, 7];
        await repository.UpsertImageAsync(CreateImageRequest(
            Guid.NewGuid(),
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes))));
        var image = (await repository.GetRecentAsync(10))
            .Single(item => item.Kind == ContentKind.Image);
        var imagePath = Path.Combine(_root, image.ContentPath!);

        var preview = await repository.PreviewCleanupAsync(null);
        var result = await repository.CleanupAsync(null);

        Assert.Equal(1, preview.TotalItems);
        Assert.Equal(1, preview.ImageItems);
        Assert.Equal(bytes.Length, preview.ImageBytes);
        Assert.Equal(1, result.DeletedImageFiles);
        Assert.False(File.Exists(imagePath));
        Assert.True(Assert.Single(await repository.GetRecentAsync(10)).IsFavorite);
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
        Assert.True(settings.DiscordSupportEnabled);
        Assert.False(settings.DiscordAccessibilityPrepared);
        settings.IsDarkTheme = true;
        settings.DiscordAccessibilityPrepared = true;
        settings.WindowLeft = 120;
        settings.WindowTop = 80;
        settings.WindowWidth = 1100;
        settings.WindowHeight = 720;
        settings.WindowMaximized = true;
        store.Save(settings);

        var restored = store.Load();
        Assert.True(restored.IsDarkTheme);
        Assert.True(restored.DiscordSupportEnabled);
        Assert.True(restored.DiscordAccessibilityPrepared);
        Assert.Equal(120, restored.WindowLeft);
        Assert.Equal(80, restored.WindowTop);
        Assert.Equal(1100, restored.WindowWidth);
        Assert.Equal(720, restored.WindowHeight);
        Assert.True(restored.WindowMaximized);
    }

    [Fact]
    public void SettingsStorePersistsAndNormalizesIntegratedFilters()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var store = new SentorySettingsStore(paths);
        store.Save(new SentorySettings
        {
            FilterDateRange = "Last7Days",
            FilterSourceApps = ["Discord", "Discord", "Unknown"]
        });

        var restored = store.Load();

        Assert.Equal("Last7Days", restored.FilterDateRange);
        Assert.Equal(["Discord"], restored.FilterSourceApps);
    }

    [Theory]
    [InlineData("ko-KR", "ko-KR")]
    [InlineData("en-us", "en-US")]
    [InlineData("ja-JP", "ja-JP")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("fr-FR", "ko-KR")]
    public void SettingsStoreNormalizesSupportedLanguage(
        string savedLanguage,
        string expectedLanguage)
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var store = new SentorySettingsStore(paths);
        store.Save(new SentorySettings
        {
            Language = savedLanguage
        });

        Assert.Equal(expectedLanguage, store.Load().Language);
    }

    [Fact]
    public void SettingsStoreQuarantinesMalformedJson()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        paths.EnsureDirectories();
        File.WriteAllText(paths.SettingsPath, "{broken json");
        var store = new SentorySettingsStore(paths);

        var settings = store.Load();

        Assert.Equal("Newest", settings.SortMode);
        Assert.False(File.Exists(paths.SettingsPath));
        Assert.Single(Directory.GetFiles(
            _root,
            "gallery-settings.corrupt-*.json"));
    }

    [Fact]
    public async Task RepeatedRepositoryRestartsPreserveContinuousCaptures()
    {
        const int captureCount = 30;

        for (var index = 0; index < captureCount; index++)
        {
            var repository = CreateRepository();
            await repository.InitializeAsync();

            if (index % 2 == 0)
            {
                await repository.UpsertUrlAsync(CreateRequest(
                    Guid.NewGuid(),
                    $"https://example.com/restart/{index}",
                    DeliveryStatus.NotObserved));
            }
            else
            {
                var bytes = new byte[]
                {
                    0x89,
                    0x50,
                    0x4E,
                    0x47,
                    (byte)index
                };
                await repository.UpsertImageAsync(CreateImageRequest(
                    Guid.NewGuid(),
                    bytes,
                    Convert.ToHexString(SHA256.HashData(bytes))));
            }
        }

        var restartedRepository = CreateRepository();
        await restartedRepository.InitializeAsync();
        var restored = await restartedRepository.GetRecentAsync(100);

        Assert.Equal(captureCount, restored.Count);
        Assert.Equal(
            captureCount / 2,
            restored.Count(item => item.Kind == ContentKind.Url));
        Assert.Equal(
            captureCount / 2,
            restored.Count(item => item.Kind == ContentKind.Image));
        Assert.Equal(
            captureCount / 2,
            Directory.GetFiles(
                SentoryDataPaths.ForRoot(_root).ImagesDirectory,
                "*.png").Length);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    [InlineData(90, 90)]
    [InlineData(180, 180)]
    [InlineData(45, 0)]
    public void SettingsStoreAllowsOnlySupportedCleanupDays(
        int savedDays,
        int expectedDays)
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        paths.EnsureDirectories();
        File.WriteAllText(
            paths.SettingsPath,
            $"{{\"AutoCleanupDays\":{savedDays}}}");

        var settings = new SentorySettingsStore(paths).Load();

        Assert.Equal(expectedDays, settings.AutoCleanupDays);
    }

    private SqliteCaptureRepository CreateRepository() =>
        new(SentoryDataPaths.ForRoot(_root));

    private static UrlCaptureRequest CreateRequest(
        Guid eventId,
        string url,
        DeliveryStatus deliveryStatus,
        DateTimeOffset? capturedAt = null)
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
            capturedAt ?? DateTimeOffset.UtcNow,
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
