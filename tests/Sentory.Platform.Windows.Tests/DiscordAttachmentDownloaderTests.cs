using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordAttachmentDownloaderTests
{
    [Fact]
    public async Task DownloadsTrustedImageAndDeduplicatesItsHash()
    {
        var png = CreatePng();
        var requestCount = 0;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(png)
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue("image/png");
            return response;
        }));
        using var downloader = new DiscordAttachmentDownloader(client);
        const string url =
            "https://cdn.discordapp.com/attachments/1/2/photo.png?token=test";

        var images = await downloader.DownloadAsync(
            [url, url, "https://example.com/photo.png"]);

        var image = Assert.Single(images);
        Assert.Equal(1, requestCount);
        Assert.Equal(png, image.ContentBytes.ToArray());
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(".png", image.FileExtension);
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
    }

    [Fact]
    public async Task RejectsResponseDeclaredLargerThanLimit()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType =
                new MediaTypeHeaderValue("image/png");
            content.Headers.ContentLength =
                DiscordAttachmentDownloader.MaximumImageBytes + 1;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content
            };
        }));
        using var downloader = new DiscordAttachmentDownloader(client);

        var images = await downloader.DownloadAsync(
        [
            "https://media.discordapp.net/attachments/1/2/photo.png"
        ]);

        Assert.Empty(images);
    }

    [Fact]
    public async Task RequestsAtMostConfiguredAttachmentCount()
    {
        var png = CreatePng();
        var requestCount = 0;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(png)
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue("image/png");
            return response;
        }));
        using var downloader = new DiscordAttachmentDownloader(client);
        var urls = Enumerable.Range(0, MaximumRequestCountForTest)
            .Select(index =>
                $"https://cdn.discordapp.com/attachments/1/{index}/photo.png")
            .ToArray();

        await downloader.DownloadAsync(urls);

        Assert.Equal(DiscordAttachmentDownloader.MaximumImagesPerBatch, requestCount);
    }

    private const int MaximumRequestCountForTest =
        DiscordAttachmentDownloader.MaximumImagesPerBatch + 5;

    private static byte[] CreatePng()
    {
        var pixels = new byte[]
        {
            0, 0, 255, 255,
            0, 255, 0, 255,
            255, 0, 0, 255,
            255, 255, 255, 255
        };
        var bitmap = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
