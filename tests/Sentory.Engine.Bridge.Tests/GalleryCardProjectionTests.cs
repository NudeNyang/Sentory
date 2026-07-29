using Sentory.Core;
using Sentory.Engine.Bridge;
using Sentory.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;

namespace Sentory.Engine.Bridge.Tests;

public sealed class GalleryCardProjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.Engine.Bridge.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Create_PrefersClearDateFileNameOverOcrMetadata()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var imagePath = CreateFile("images/VRChat_2025-07-30_01-40-52.png");
        var item = CreateImage(
            Path.GetRelativePath(_root, imagePath),
            "VRChat_2025-07-30_01-40-52.png",
            "applicationVRCX,version1,author");

        var card = GalleryCardProjection.Create(item, paths);

        Assert.Equal("VRChat_2025-07-30_01-40-52", card.Title);
        Assert.Equal(imagePath, card.ArtworkPath);
        Assert.Equal("contain", card.ArtworkMode);
    }

    [Fact]
    public void ResolveStoredPath_RejectsFileOutsideDataRoot()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(
            Path.GetDirectoryName(_root)!,
            $"outside-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(outside, [1, 2, 3]);

        try
        {
            var relative = Path.GetRelativePath(_root, outside);

            Assert.Null(GalleryCardProjection.ResolveStoredPath(relative, paths));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void Create_UsesExistingWpfCardThumbnailInsteadOfOriginalImage()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var imagePath = CreateFile("images/photo.png");
        var info = new FileInfo(imagePath);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))
            .ToLowerInvariant();
        var thumbnailPath = CreateFile(
            $"cache/gallery-card-thumbnails/v3/{key}.jpg");
        var item = CreateImage(
            Path.GetRelativePath(_root, imagePath),
            "photo.png",
            "사진");

        var card = GalleryCardProjection.Create(item, paths);

        Assert.Equal(thumbnailPath, card.ArtworkPath);
        Assert.Equal("contain", card.ArtworkMode);
    }

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        return Path.GetFullPath(path);
    }

    private static CapturedItemSummary CreateImage(
        string contentPath,
        string originalUrl,
        string ocrDisplayName) =>
        new(
            Guid.NewGuid(),
            ContentKind.Image,
            originalUrl,
            originalUrl,
            string.Empty,
            SourceApp.Line,
            CaptureMethod.LineConfirmedImage,
            DeliveryStatus.Confirmed,
            1,
            1,
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            ContentPath: contentPath,
            MimeType: "image/png",
            OcrDisplayName: ocrDisplayName);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
