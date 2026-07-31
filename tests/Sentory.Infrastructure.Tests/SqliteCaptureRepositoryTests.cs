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
                "image/png",
                ".png",
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
    public async Task ImageKeepsOriginalFileNameWhenLaterCaptureHasNoFileName()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        byte[] bytes = [11, 22, 33, 44];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var namedRequest = CreateImageRequest(Guid.NewGuid(), bytes, hash) with
        {
            OriginalFileName = "VRChat 2025-01-28 23-02-56.776 1080x1920.png"
        };

        await repository.UpsertImageAsync(namedRequest);
        await repository.UpsertImageAsync(
            CreateImageRequest(Guid.NewGuid(), bytes, hash));
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal(namedRequest.OriginalFileName, item.OriginalUrl);
    }

    [Fact]
    public async Task ImageKeepsOriginalFileNameWhenLaterCaptureUsesStorageHashName()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        byte[] bytes = [11, 22, 33, 44];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        const string originalFileName =
            "VRChat 2026-07-26 23-18-47.png";

        await repository.UpsertImageAsync(
            CreateImageRequest(Guid.NewGuid(), bytes, hash) with
            {
                OriginalFileName = originalFileName
            });
        await repository.UpsertImageAsync(
            CreateImageRequest(Guid.NewGuid(), bytes, hash) with
            {
                OriginalFileName = $"{hash.ToLowerInvariant()}.png"
            });
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal(originalFileName, item.OriginalUrl);
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
    public async Task CapturesWithinSixHoursCountAsOneUsageSession()
    {
        var repository = CreateRepository();
        repository.ConfigureAutomaticFavorites(
            enabled: true,
            usageThreshold: 2);
        await repository.InitializeAsync();
        var firstCapturedAt = DateTimeOffset.UtcNow.AddDays(-1);

        var first = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/session",
            DeliveryStatus.Confirmed,
            firstCapturedAt));
        var second = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/session",
            DeliveryStatus.Confirmed,
            firstCapturedAt.AddHours(5)));
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal(1, first.RecentUsageSessionCount);
        Assert.Equal(1, second.RecentUsageSessionCount);
        Assert.False(item.IsFavorite);
    }

    [Fact]
    public async Task SeparateUsageSessionsAutomaticallyAddFavorite()
    {
        var repository = CreateRepository();
        repository.ConfigureAutomaticFavorites(
            enabled: true,
            usageThreshold: 2);
        await repository.InitializeAsync();
        var firstCapturedAt = DateTimeOffset.UtcNow.AddDays(-1);
        byte[] bytes = [3, 1, 4, 1, 5];
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        var first = await repository.UpsertImageAsync(
            CreateImageRequest(Guid.NewGuid(), bytes, hash) with
            {
                CapturedAt = firstCapturedAt
            });
        var second = await repository.UpsertImageAsync(
            CreateImageRequest(Guid.NewGuid(), bytes, hash) with
            {
                CapturedAt = firstCapturedAt.AddHours(7)
            });
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal(1, first.RecentUsageSessionCount);
        Assert.Equal(2, second.RecentUsageSessionCount);
        Assert.True(item.IsFavorite);
    }

    [Fact]
    public async Task UsageSessionOlderThanThirtyDaysDoesNotAddFavorite()
    {
        var repository = CreateRepository();
        repository.ConfigureAutomaticFavorites(
            enabled: true,
            usageThreshold: 2);
        await repository.InitializeAsync();
        var firstCapturedAt = DateTimeOffset.UtcNow.AddDays(-31);

        await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/old-session",
            DeliveryStatus.Confirmed,
            firstCapturedAt));
        var recent = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/old-session",
            DeliveryStatus.Confirmed,
            firstCapturedAt.AddDays(31)));
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal(1, recent.RecentUsageSessionCount);
        Assert.False(item.IsFavorite);
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

        Assert.Equal(7L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task VersionSixDatabaseBackfillsFavoriteChangeTime()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        var captured = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/favorite-clock",
            DeliveryStatus.NotObserved));
        Assert.True(await repository.SetFavoriteAsync(
            captured.ItemId,
            true));
        await using (var connection = new SqliteConnection(
                         $"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var downgrade = connection.CreateCommand();
            downgrade.CommandText =
                """
                UPDATE items SET favorite_changed_at = NULL;
                PRAGMA user_version = 6;
                """;
            await downgrade.ExecuteNonQueryAsync();
        }

        await repository.InitializeAsync();
        await using var verifyConnection = new SqliteConnection(
            $"Data Source={paths.DatabasePath}");
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText =
            "SELECT favorite_changed_at FROM items WHERE id = $itemId;";
        verify.Parameters.AddWithValue(
            "$itemId",
            captured.ItemId.ToString("D"));

        Assert.False(string.IsNullOrWhiteSpace(Convert.ToString(
            await verify.ExecuteScalarAsync())));
    }

    [Fact]
    public async Task VersionFiveDatabaseBackfillsUsageSessions()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        var firstCapturedAt = DateTimeOffset.UtcNow.AddDays(-2);
        await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/backfill",
            DeliveryStatus.Confirmed,
            firstCapturedAt));
        await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/backfill",
            DeliveryStatus.Confirmed,
            firstCapturedAt.AddHours(7)));
        await using (var connection = new SqliteConnection(
                         $"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DROP TABLE usage_sessions;
                PRAGMA user_version = 5;
                """;
            await command.ExecuteNonQueryAsync();
        }

        var migrated = new SqliteCaptureRepository(paths);
        migrated.ConfigureAutomaticFavorites(
            enabled: true,
            usageThreshold: 2);
        await migrated.InitializeAsync();
        var item = Assert.Single(await migrated.GetRecentAsync(10));

        Assert.True(item.IsFavorite);
    }

    [Fact]
    public async Task VersionFourDatabaseAddsImageOcrStorage()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        await using (var connection = new SqliteConnection(
                         $"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DROP TABLE image_ocr;
                PRAGMA user_version = 4;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await repository.InitializeAsync();
        await using var migrated = new SqliteConnection(
            $"Data Source={paths.DatabasePath}");
        await migrated.OpenAsync();
        await using var inspect = migrated.CreateCommand();
        inspect.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table' AND name = 'image_ocr';
            """;

        Assert.Equal(1L, (long)(await inspect.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task VersionFourRetriesUnavailableYouTubePreviews()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        var youtube = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://www.youtube.com/watch?v=gqkfH78Gm40",
            DeliveryStatus.Confirmed));
        var ordinary = await repository.UpsertUrlAsync(CreateRequest(
            Guid.NewGuid(),
            "https://example.com/unavailable",
            DeliveryStatus.Confirmed));
        var fetchedAt = DateTimeOffset.UtcNow;
        foreach (var itemId in new[] { youtube.ItemId, ordinary.ItemId })
        {
            Assert.True(await repository.UpdateLinkPreviewAsync(
                itemId,
                new LinkPreviewUpdate(
                    LinkPreviewStatus.Unavailable,
                    null,
                    null,
                    null,
                    null,
                    fetchedAt)));
        }

        await using (var connection = new SqliteConnection(
                         $"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 3;";
            await command.ExecuteNonQueryAsync();
        }

        await repository.InitializeAsync();
        var candidates = await repository.GetLinkPreviewCandidatesAsync(
            10,
            fetchedAt.AddYears(-1));

        var candidate = Assert.Single(candidates);
        Assert.Equal(youtube.ItemId, candidate.ItemId);
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
    public async Task CollectionUsesItsFirstLinkAsRepresentativePreview()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var result = await repository.UpsertCollectionAsync(
            CreateUrlOnlyCollectionRequest(Guid.NewGuid()));
        var fetchedAt = DateTimeOffset.UtcNow;

        var candidates = await repository.GetLinkPreviewCandidatesAsync(
            10,
            fetchedAt.AddDays(-30));
        var candidate = Assert.Single(candidates);

        Assert.Equal(result.ItemId, candidate.ItemId);
        Assert.Equal("https://example.com/first", candidate.Url);
        Assert.Equal("https://example.com/first", candidate.NormalizedKey);
        Assert.True(await repository.UpdateLinkPreviewAsync(
            result.ItemId,
            new LinkPreviewUpdate(
                LinkPreviewStatus.Available,
                "First link",
                "Representative collection link",
                "link-previews/collection-icon.png",
                "link-previews/collection-cover.jpg",
                fetchedAt)));

        var item = Assert.Single(await repository.GetRecentAsync(10));
        Assert.Equal(ContentKind.Collection, item.Kind);
        Assert.Equal("First link", item.PageTitle);
        Assert.Equal(
            "link-previews/collection-cover.jpg",
            item.PreviewImagePath);
        Assert.Empty(await repository.GetLinkPreviewCandidatesAsync(
            10,
            fetchedAt.AddDays(-30)));
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
        Assert.Equal("Light", settings.ThemeMode);
        Assert.True(settings.MessengerDetectionSetupCompleted);
        Assert.True(settings.DiscordSupportEnabled);
        Assert.True(settings.KakaoTalkSupportEnabled);
        Assert.True(settings.SlackSupportEnabled);
        Assert.True(settings.WhatsAppSupportEnabled);
        Assert.True(settings.TelegramSupportEnabled);
        Assert.True(settings.LineSupportEnabled);
        Assert.True(settings.WeChatSupportEnabled);
        Assert.False(settings.DiscordAccessibilityPrepared);
        Assert.Null(settings.StartWithWindows);
        settings.IsDarkTheme = true;
        settings.ThemeMode = "Dark";
        settings.DiscordAccessibilityPrepared = true;
        settings.StartWithWindows = false;
        settings.WindowLeft = 120;
        settings.WindowTop = 80;
        settings.WindowWidth = 1100;
        settings.WindowHeight = 720;
        settings.WindowMaximized = true;
        store.Save(settings);

        var restored = store.Load();
        Assert.True(restored.IsDarkTheme);
        Assert.Equal("Dark", restored.ThemeMode);
        Assert.True(restored.MessengerDetectionSetupCompleted);
        Assert.True(restored.DiscordSupportEnabled);
        Assert.True(restored.KakaoTalkSupportEnabled);
        Assert.True(restored.SlackSupportEnabled);
        Assert.True(restored.WhatsAppSupportEnabled);
        Assert.True(restored.TelegramSupportEnabled);
        Assert.True(restored.LineSupportEnabled);
        Assert.True(restored.WeChatSupportEnabled);
        Assert.True(restored.DiscordAccessibilityPrepared);
        Assert.False(restored.StartWithWindows);
        Assert.Equal(120, restored.WindowLeft);
        Assert.Equal(80, restored.WindowTop);
        Assert.Equal(1100, restored.WindowWidth);
        Assert.Equal(720, restored.WindowHeight);
        Assert.True(restored.WindowMaximized);
    }

    [Fact]
    public void SettingsStoreMigratesLegacyDarkThemeSelection()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        paths.EnsureDirectories();
        File.WriteAllText(
            paths.SettingsPath,
            """{"IsDarkTheme":true}""");

        var settings = new SentorySettingsStore(paths).Load();

        Assert.Equal("Dark", settings.ThemeMode);
        Assert.True(settings.IsDarkTheme);
    }

    [Fact]
    public void SettingsStorePersistsSystemThemeSelection()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var store = new SentorySettingsStore(paths);
        store.Save(new SentorySettings
        {
            ThemeMode = "System",
            IsDarkTheme = true
        });

        var restored = store.Load();

        Assert.Equal("System", restored.ThemeMode);
        Assert.True(restored.IsDarkTheme);
    }

    [Fact]
    public void SettingsStorePersistsIndependentMessengerDetectionStates()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var store = new SentorySettingsStore(paths);
        store.Save(new SentorySettings
        {
            DiscordSupportEnabled = false,
            KakaoTalkSupportEnabled = true,
            SlackSupportEnabled = false,
            WhatsAppSupportEnabled = false,
            TelegramSupportEnabled = false,
            LineSupportEnabled = false
        });

        var restored = store.Load();

        Assert.False(restored.DiscordSupportEnabled);
        Assert.True(restored.KakaoTalkSupportEnabled);
        Assert.False(restored.SlackSupportEnabled);
        Assert.False(restored.WhatsAppSupportEnabled);
        Assert.False(restored.TelegramSupportEnabled);
        Assert.False(restored.LineSupportEnabled);
    }

    [Fact]
    public void SettingsStorePersistsAndNormalizesIntegratedFilters()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var store = new SentorySettingsStore(paths);
        store.Save(new SentorySettings
        {
            FilterDateRange = "Last7Days",
            FilterSourceApps =
                ["Discord", "Telegram", "Discord", "Unknown"]
        });

        var restored = store.Load();

        Assert.Equal("Last7Days", restored.FilterDateRange);
        Assert.Equal(["Discord", "Telegram"], restored.FilterSourceApps);
    }

    [Theory]
    [InlineData("ko-KR", "auto")]
    [InlineData("en-us", "en-US")]
    [InlineData("ja-JP", "ja-JP")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("fr-FR", "auto")]
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

    [Fact]
    public async Task CollectionIsOneCardDeduplicatedBySignatureAndCopiesItsMembers()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        byte[] imageBytes = [9, 8, 7, 6];
        var hash = Convert.ToHexString(SHA256.HashData(imageBytes));
        var first = CreateCollectionRequest(Guid.NewGuid(), imageBytes, hash);
        var second = first with { EventId = Guid.NewGuid() };

        var firstResult = await repository.UpsertCollectionAsync(first);
        var secondResult = await repository.UpsertCollectionAsync(second);
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.True(firstResult.ItemCreated);
        Assert.False(secondResult.ItemCreated);
        Assert.Equal(ContentKind.Collection, item.Kind);
        Assert.Equal(2, item.CaptureCount);
        Assert.Equal(2, item.ShareCount);
        Assert.Equal(2, item.Members?.Count);
        Assert.Single(item.Members!, member => member.Kind == ContentKind.Url);
        var image = Assert.Single(
            item.Members!,
            member => member.Kind == ContentKind.Image);
        Assert.NotNull(image.ContentPath);
        Assert.True(File.Exists(Path.Combine(_root, image.ContentPath)));
    }

    [Fact]
    public async Task CollectionKeepsImageFileNameWhenLaterCaptureHasNoName()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        byte[] imageBytes = [7, 6, 5, 4];
        var hash = Convert.ToHexString(SHA256.HashData(imageBytes));
        var original = CreateCollectionRequest(Guid.NewGuid(), imageBytes, hash);
        var namedMembers = original.Members
            .Select(member => member.Kind == ContentKind.Image
                ? member with { OriginalUrl = "여행 사진 2025.png" }
                : member)
            .ToArray();
        var named = original with { Members = namedMembers };
        var unnamed = original with { EventId = Guid.NewGuid() };

        await repository.UpsertCollectionAsync(named);
        await repository.UpsertCollectionAsync(unnamed);
        var item = Assert.Single(await repository.GetRecentAsync(10));
        var image = Assert.Single(item.Members!, member =>
            member.Kind == ContentKind.Image);

        Assert.Equal("여행 사진 2025.png", image.OriginalUrl);
    }

    [Fact]
    public async Task DeletingCollectionRemovesItsUnreferencedImage()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        byte[] imageBytes = [4, 5, 6, 7];
        var hash = Convert.ToHexString(SHA256.HashData(imageBytes));
        await repository.UpsertCollectionAsync(
            CreateCollectionRequest(Guid.NewGuid(), imageBytes, hash));
        var item = Assert.Single(await repository.GetRecentAsync(10));
        var imagePath = Path.Combine(
            _root,
            Assert.Single(item.Members!, member => member.Kind == ContentKind.Image)
                .ContentPath!);

        Assert.True(await repository.DeleteItemAsync(item.ItemId));

        Assert.False(File.Exists(imagePath));
    }

    [Fact]
    public async Task DeletingCollectionKeepsImageReferencedByAnotherCard()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        byte[] imageBytes = [1, 3, 5, 7, 9];
        var hash = Convert.ToHexString(SHA256.HashData(imageBytes));
        await repository.UpsertImageAsync(
            CreateImageRequest(Guid.NewGuid(), imageBytes, hash));
        await repository.UpsertCollectionAsync(
            CreateCollectionRequest(Guid.NewGuid(), imageBytes, hash));
        var items = await repository.GetRecentAsync(10);
        var collection = Assert.Single(
            items,
            item => item.Kind == ContentKind.Collection);
        var image = Assert.Single(
            items,
            item => item.Kind == ContentKind.Image);
        var imagePath = Path.Combine(_root, image.ContentPath!);

        Assert.True(await repository.DeleteItemAsync(collection.ItemId));
        Assert.True(File.Exists(imagePath));

        Assert.True(await repository.DeleteItemAsync(image.ItemId));
        Assert.False(File.Exists(imagePath));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
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
            "image/png",
            ".png",
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVImage,
            DeliveryStatus.NotObserved,
            "test-context",
            DateTimeOffset.UtcNow,
            ["test"]);

    private static CollectionCaptureRequest CreateCollectionRequest(
        Guid eventId,
        byte[] imageBytes,
        string hash) =>
        new(
            eventId,
            CaptureCollectionIdentity.CreateSignature(
            [
                new CollectionMemberCaptureRequest(
                    ContentKind.Url,
                    "https://example.com/path",
                    "https://example.com/path",
                    "example.com",
                    ReadOnlyMemory<byte>.Empty,
                    null,
                    0,
                    0,
                    null,
                    null),
                new CollectionMemberCaptureRequest(
                    ContentKind.Image,
                    string.Empty,
                    $"sha256:{hash.ToLowerInvariant()}",
                    string.Empty,
                    imageBytes,
                    hash,
                    2,
                    2,
                    "image/png",
                    ".png")
            ]),
            [
                new CollectionMemberCaptureRequest(
                    ContentKind.Url,
                    "https://example.com/path",
                    "https://example.com/path",
                    "example.com",
                    ReadOnlyMemory<byte>.Empty,
                    null,
                    0,
                    0,
                    null,
                    null),
                new CollectionMemberCaptureRequest(
                    ContentKind.Image,
                    string.Empty,
                    $"sha256:{hash.ToLowerInvariant()}",
                    string.Empty,
                    imageBytes,
                    hash,
                    2,
                    2,
                    "image/png",
                    ".png")
            ],
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedImage,
            DeliveryStatus.Confirmed,
            "discord-context",
            DateTimeOffset.UtcNow,
            ["test"]);

    private static CollectionCaptureRequest CreateUrlOnlyCollectionRequest(
        Guid eventId)
    {
        CollectionMemberCaptureRequest[] members =
        [
            new(
                ContentKind.Url,
                "https://example.com/first",
                "https://example.com/first",
                "example.com",
                ReadOnlyMemory<byte>.Empty,
                null,
                0,
                0,
                null,
                null),
            new(
                ContentKind.Url,
                "https://example.org/second",
                "https://example.org/second",
                "example.org",
                ReadOnlyMemory<byte>.Empty,
                null,
                0,
                0,
                null,
                null)
        ];
        return new CollectionCaptureRequest(
            eventId,
            CaptureCollectionIdentity.CreateSignature(members),
            members,
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVUrl,
            DeliveryStatus.NotObserved,
            "test-context",
            DateTimeOffset.UtcNow,
            ["test"]);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
