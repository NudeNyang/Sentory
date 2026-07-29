using System.Security.Cryptography;
using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Tests;

public sealed class GalleryPageRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "sentory-gallery-page-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GalleryPageAppliesPagingFiltersAndSortInSql()
    {
        var repository = new SqliteCaptureRepository(
            SentoryDataPaths.ForRoot(_root));
        await repository.InitializeAsync();
        var now = new DateTimeOffset(2026, 7, 29, 18, 0, 0, TimeSpan.FromHours(9));

        var old = await repository.UpsertUrlAsync(CreateUrl(
            "https://old.example/item",
            SourceApp.Discord,
            now.AddDays(-4)));
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var imageHash = Convert.ToHexString(SHA256.HashData(imageBytes));
        var image = await repository.UpsertImageAsync(new ImageCaptureRequest(
            Guid.NewGuid(),
            imageBytes,
            imageHash,
            40,
            30,
            "image/png",
            ".png",
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVImage,
            DeliveryStatus.NotObserved,
            "image-context",
            now.AddDays(-3),
            ["test"]));
        var middle = await repository.UpsertUrlAsync(CreateUrl(
            "https://middle.example/item",
            SourceApp.KakaoTalk,
            now.AddDays(-2)));
        var latest = await repository.UpsertUrlAsync(CreateUrl(
            "https://latest.example/item",
            SourceApp.Discord,
            now.AddDays(-1)));
        await repository.SetFavoriteAsync(middle.ItemId, true);
        await repository.RecordCopyAsync(old.ItemId, now.AddMinutes(-2));
        await repository.RecordCopyAsync(old.ItemId, now.AddMinutes(-1));

        var page = await repository.GetGalleryPageAsync(Request(
            now,
            offset: 1,
            limit: 2));
        Assert.Equal(4, page.Total);
        Assert.Equal(
            [middle.ItemId, image.ItemId],
            page.Items.Select(item => item.ItemId));

        var photos = await repository.GetGalleryPageAsync(Request(
            now,
            kind: ContentKind.Image));
        Assert.Equal(1, photos.Total);
        Assert.Equal(image.ItemId, Assert.Single(photos.Items).ItemId);

        var searched = await repository.GetGalleryPageAsync(Request(
            now,
            search: "middle.example"));
        Assert.Equal(1, searched.Total);
        Assert.Equal(middle.ItemId, Assert.Single(searched.Items).ItemId);

        var favorite = await repository.GetGalleryPageAsync(Request(
            now,
            favoritesOnly: true));
        Assert.Equal(1, favorite.Total);
        Assert.Equal(middle.ItemId, Assert.Single(favorite.Items).ItemId);

        var discord = await repository.GetGalleryPageAsync(Request(
            now,
            sources: new HashSet<SourceApp> { SourceApp.Discord }));
        Assert.Equal(2, discord.Total);
        Assert.Equal(
            [latest.ItemId, old.ItemId],
            discord.Items.Select(item => item.ItemId));

        var mostCopied = await repository.GetGalleryPageAsync(Request(
            now,
            sort: GallerySortMode.MostCopied));
        Assert.Equal(old.ItemId, mostCopied.Items[0].ItemId);
    }

    private static GalleryPageRequest Request(
        DateTimeOffset now,
        int offset = 0,
        int limit = 20,
        ContentKind? kind = null,
        string search = "",
        GallerySortMode sort = GallerySortMode.Newest,
        bool favoritesOnly = false,
        IReadOnlySet<SourceApp>? sources = null) =>
        new(
            new GalleryQueryOptions(
                kind,
                search,
                GalleryDateRange.All,
                sort,
                favoritesOnly,
                sources),
            offset,
            limit,
            now);

    private static UrlCaptureRequest CreateUrl(
        string url,
        SourceApp source,
        DateTimeOffset capturedAt)
    {
        Assert.True(UrlNormalizer.TryNormalize(url, out var normalized));
        return new UrlCaptureRequest(
            Guid.NewGuid(),
            url,
            normalized,
            source,
            source == SourceApp.Discord
                ? CaptureMethod.DiscordConfirmedSend
                : CaptureMethod.KakaoCtrlVUrl,
            source == SourceApp.Discord
                ? DeliveryStatus.Confirmed
                : DeliveryStatus.NotObserved,
            "gallery-page-context",
            capturedAt,
            ["test"]);
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
