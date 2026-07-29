using Sentory.Core;

namespace Sentory.App.Tests;

public sealed class WebGalleryRendererPolicyTests
{
    [Theory]
    [InlineData(true, "WebView2", true)]
    [InlineData(true, " webview2 ", true)]
    [InlineData(true, "Wpf", false)]
    [InlineData(true, null, false)]
    [InlineData(false, "WebView2", false)]
    public void EnablesOnlyExplicitDeveloperOptIn(
        bool developerBuild,
        string? value,
        bool expected)
    {
        Assert.Equal(
            expected,
            WebGalleryRendererPolicy.IsEnabled(developerBuild, value));
    }
}

public sealed class WebGalleryRangePolicyTests
{
    [Theory]
    [InlineData(0, 24, 100, 0, 24)]
    [InlineData(-5, 24, 100, 0, 24)]
    [InlineData(95, 24, 100, 95, 5)]
    [InlineData(0, 500, 1000, 0, 120)]
    [InlineData(100, 24, 100, 100, 0)]
    [InlineData(0, 24, 0, 0, 0)]
    public void ClampsRequestedViewportRange(
        int start,
        int count,
        int total,
        int expectedStart,
        int expectedCount)
    {
        Assert.Equal(
            (expectedStart, expectedCount),
            WebGalleryRangePolicy.Clamp(start, count, total));
    }
}

public sealed class WebGalleryAssetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"sentory-web-gallery-assets-{Guid.NewGuid():N}");

    [Fact]
    public void MaterializesEveryEmbeddedGalleryAsset()
    {
        var directory = new WebGalleryAssetStore(_root).Materialize();

        Assert.True(File.Exists(Path.Combine(directory, "index.html")));
        Assert.True(File.Exists(Path.Combine(directory, "gallery.css")));
        Assert.True(File.Exists(Path.Combine(directory, "gallery.js")));
        Assert.Contains(
            "Content-Security-Policy",
            File.ReadAllText(Path.Combine(directory, "index.html")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

public sealed class WebGalleryArtworkPolicyTests
{
    [Fact]
    public void UsesDisplayThumbnailForStoredImage()
    {
        var artwork = WebGalleryArtworkPolicy.Resolve(
            CreateItem(ContentKind.Image, contentPath: "images/photo.png"));

        Assert.Equal(
            new WebGalleryArtworkCandidate(
                "images/photo.png",
                "contain",
                CreateCardThumbnail: true),
            artwork);
    }

    [Fact]
    public void PrefersCollectionImageThenLinkPreviewAndIcon()
    {
        var member = new CapturedCollectionMember(
            0,
            ContentKind.Image,
            string.Empty,
            "image-key",
            string.Empty,
            "images/member.png",
            "hash",
            "image/png",
            100,
            100);
        var collectionArtwork = WebGalleryArtworkPolicy.Resolve(
            CreateItem(
                ContentKind.Collection,
                previewImagePath: "link-previews/cover.jpg",
                siteIconPath: "link-previews/icon.png",
                members: [member]));
        var previewArtwork = WebGalleryArtworkPolicy.Resolve(
            CreateItem(
                ContentKind.Url,
                previewImagePath: "link-previews/cover.jpg",
                siteIconPath: "link-previews/icon.png"));
        var iconArtwork = WebGalleryArtworkPolicy.Resolve(
            CreateItem(
                ContentKind.Url,
                siteIconPath: "link-previews/icon.png"));

        Assert.Equal("images/member.png", collectionArtwork?.RelativePath);
        Assert.Equal("contain", collectionArtwork?.Mode);
        Assert.Equal("link-previews/cover.jpg", previewArtwork?.RelativePath);
        Assert.Equal("cover", previewArtwork?.Mode);
        Assert.Equal("link-previews/icon.png", iconArtwork?.RelativePath);
        Assert.Equal("icon", iconArtwork?.Mode);
    }

    private static CapturedItemSummary CreateItem(
        ContentKind kind,
        string? contentPath = null,
        string? previewImagePath = null,
        string? siteIconPath = null,
        IReadOnlyList<CapturedCollectionMember>? members = null) =>
        new(
            Guid.NewGuid(),
            kind,
            string.Empty,
            "key",
            string.Empty,
            SourceApp.Discord,
            CaptureMethod.DiscordConfirmedSend,
            DeliveryStatus.Confirmed,
            1,
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            ContentPath: contentPath,
            SiteIconPath: siteIconPath,
            PreviewImagePath: previewImagePath,
            Members: members);
}
