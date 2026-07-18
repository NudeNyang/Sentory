using Sentory.Core;

namespace Sentory.Core.Tests;

public sealed class GalleryQueryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 18, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void SortsNewestAndOldestByLastCaptureTime()
    {
        var older = Create("older.example", Now.AddDays(-2));
        var newer = Create("newer.example", Now.AddHours(-1));

        var newest = Apply([older, newer], GallerySortMode.Newest);
        var oldest = Apply([older, newer], GallerySortMode.Oldest);

        Assert.Equal([newer.ItemId, older.ItemId],
            newest.Select(item => item.ItemId));
        Assert.Equal([older.ItemId, newer.ItemId],
            oldest.Select(item => item.ItemId));
    }

    [Fact]
    public void SortsMostCapturedWithRecentTieBreaker()
    {
        var frequent = Create(
            "frequent.example",
            Now.AddDays(-3),
            captureCount: 5);
        var recent = Create(
            "recent.example",
            Now.AddHours(-1),
            captureCount: 2);

        var results = Apply(
            [recent, frequent],
            GallerySortMode.MostCaptured);

        Assert.Equal(frequent.ItemId, results[0].ItemId);
    }

    [Fact]
    public void SortsUrlsByDomainName()
    {
        var zebra = Create("zebra.example", Now);
        var alpha = Create("alpha.example", Now.AddMinutes(-1));

        var results = Apply([zebra, alpha], GallerySortMode.Name);

        Assert.Equal([alpha.ItemId, zebra.ItemId],
            results.Select(item => item.ItemId));
    }

    [Fact]
    public void SortsByCopyUsage()
    {
        var oftenCopied = Create(
            "often.example",
            Now.AddDays(-2),
            copyCount: 4,
            lastCopiedAt: Now.AddDays(-1));
        var recentlyCopied = Create(
            "recent-copy.example",
            Now.AddDays(-1),
            copyCount: 1,
            lastCopiedAt: Now.AddMinutes(-5));
        var neverCopied = Create("never.example", Now);

        var mostCopied = Apply(
            [neverCopied, recentlyCopied, oftenCopied],
            GallerySortMode.MostCopied);
        var recentCopies = Apply(
            [neverCopied, oftenCopied, recentlyCopied],
            GallerySortMode.RecentlyCopied);

        Assert.Equal(oftenCopied.ItemId, mostCopied[0].ItemId);
        Assert.Equal(recentlyCopied.ItemId, recentCopies[0].ItemId);
        Assert.Equal(neverCopied.ItemId, recentCopies[^1].ItemId);
    }

    [Fact]
    public void FiltersFavoritesAcrossContentKinds()
    {
        var favoriteUrl = Create(
            "favorite.example",
            Now,
            isFavorite: true);
        var favoriteImage = Create(
            string.Empty,
            Now,
            kind: ContentKind.Image,
            isFavorite: true);
        var other = Create("other.example", Now);

        var results = GalleryQuery.Apply(
            [other, favoriteImage, favoriteUrl],
            new GalleryQueryOptions(
                null,
                string.Empty,
                GalleryDateRange.All,
                GallerySortMode.Newest,
                FavoritesOnly: true),
            Now);

        Assert.Equal(
            new[] { favoriteImage.ItemId, favoriteUrl.ItemId }.Order(),
            results.Select(item => item.ItemId).Order());
    }

    [Theory]
    [InlineData(GalleryDateRange.Today, 1)]
    [InlineData(GalleryDateRange.Last7Days, 2)]
    [InlineData(GalleryDateRange.Last30Days, 3)]
    [InlineData(GalleryDateRange.All, 4)]
    public void FiltersByDateRange(
        GalleryDateRange range,
        int expectedCount)
    {
        var items = new[]
        {
            Create("today.example", Now.AddHours(-1)),
            Create("week.example", Now.AddDays(-6)),
            Create("month.example", Now.AddDays(-20)),
            Create("old.example", Now.AddDays(-60))
        };

        var results = GalleryQuery.Apply(
            items,
            new GalleryQueryOptions(
                null,
                string.Empty,
                range,
                GallerySortMode.Newest),
            Now);

        Assert.Equal(expectedCount, results.Count);
    }

    [Fact]
    public void CombinesKindAndSearchFilters()
    {
        var match = Create("wanted.example", Now);
        var other = Create("other.example", Now);
        var image = Create(
            string.Empty,
            Now,
            kind: ContentKind.Image);

        var results = GalleryQuery.Apply(
            [other, image, match],
            new GalleryQueryOptions(
                ContentKind.Url,
                "wanted",
                GalleryDateRange.All,
                GallerySortMode.Newest),
            Now);

        Assert.Equal(match.ItemId, Assert.Single(results).ItemId);
    }

    [Fact]
    public void SearchesLinkPreviewTitleAndDescription()
    {
        var titleMatch = Create("one.example", Now) with
        {
            PageTitle = "Sentory guide"
        };
        var descriptionMatch = Create("two.example", Now) with
        {
            PageDescription = "A useful clipboard archive"
        };
        var other = Create("other.example", Now);

        var titleResults = GalleryQuery.Apply(
            [other, descriptionMatch, titleMatch],
            new GalleryQueryOptions(
                ContentKind.Url,
                "guide",
                GalleryDateRange.All,
                GallerySortMode.Newest),
            Now);
        var descriptionResults = GalleryQuery.Apply(
            [other, descriptionMatch, titleMatch],
            new GalleryQueryOptions(
                ContentKind.Url,
                "clipboard archive",
                GalleryDateRange.All,
                GallerySortMode.Newest),
            Now);

        Assert.Equal(titleMatch.ItemId, Assert.Single(titleResults).ItemId);
        Assert.Equal(
            descriptionMatch.ItemId,
            Assert.Single(descriptionResults).ItemId);
    }

    private static IReadOnlyList<CapturedItemSummary> Apply(
        IEnumerable<CapturedItemSummary> items,
        GallerySortMode sortMode) =>
        GalleryQuery.Apply(
            items,
            new GalleryQueryOptions(
                null,
                string.Empty,
                GalleryDateRange.All,
                sortMode),
            Now);

    private static CapturedItemSummary Create(
        string domain,
        DateTimeOffset lastCapturedAt,
        int captureCount = 1,
        ContentKind kind = ContentKind.Url,
        bool isFavorite = false,
        int copyCount = 0,
        DateTimeOffset? lastCopiedAt = null)
    {
        var key = kind == ContentKind.Url
            ? $"https://{domain}/"
            : $"sha256:{Guid.NewGuid():N}";
        return new CapturedItemSummary(
            Guid.NewGuid(),
            kind,
            kind == ContentKind.Url ? key : string.Empty,
            key,
            domain,
            SourceApp.KakaoTalk,
            kind == ContentKind.Url
                ? CaptureMethod.KakaoCtrlVUrl
                : CaptureMethod.KakaoCtrlVImage,
            DeliveryStatus.NotObserved,
            captureCount,
            0,
            lastCapturedAt.AddDays(-1),
            lastCapturedAt,
            IsFavorite: isFavorite,
            CopyCount: copyCount,
            LastCopiedAt: lastCopiedAt);
    }
}
