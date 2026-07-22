using System.Net;
using System.Net.Http.Headers;
using Microsoft.Data.Sqlite;
using Sentory.Core;
using Sentory.Infrastructure.Data;
using Sentory.Infrastructure.Links;

namespace Sentory.Infrastructure.Tests;

public sealed class LinkPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "Sentory.LinkPreview.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ParserExtractsOpenGraphAndResolvesRelativeAssets()
    {
        const string html =
            """
            <html><head>
              <base href="https://cdn.example.com/articles/">
              <meta property="og:title" content="  Test &amp; title  ">
              <meta name="description" content="Short description">
              <meta property="og:image" content="cover.jpg">
              <link rel="shortcut icon" href="/favicon.png">
              <title>Fallback title</title>
            </head></html>
            """;

        var parsed = LinkPreviewHtmlParser.Parse(
            html,
            new Uri("https://example.com/post/1"));

        Assert.Equal("Test & title", parsed.Title);
        Assert.Equal("Short description", parsed.Description);
        Assert.Equal(
            new Uri("https://cdn.example.com/favicon.png"),
            parsed.SiteIconUri);
        Assert.Equal(
            new Uri("https://cdn.example.com/articles/cover.jpg"),
            parsed.PreviewImageUri);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://192.168.1.2/")]
    [InlineData("http://[::1]/")]
    [InlineData("https://user:password@example.com/")]
    public async Task UriPolicyRejectsPrivateOrCredentialedAddresses(string value)
    {
        var allowed = await LinkPreviewUriPolicy.IsAllowedAsync(
            new Uri(value),
            new FixedResolver(IPAddress.Parse("93.184.216.34")),
            CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task UriPolicyRejectsHostWhenAnyDnsAnswerIsPrivate()
    {
        var allowed = await LinkPreviewUriPolicy.IsAllowedAsync(
            new Uri("https://rebind.example/path"),
            new FixedResolver(
                IPAddress.Parse("93.184.216.34"),
                IPAddress.Loopback),
            CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task ConnectionStepRejectsPrivateDnsRebindingAnswer()
    {
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await LinkPreviewFetcher.ConnectToPublicEndpointAsync(
                new DnsEndPoint("rebind.example", 443),
                new FixedResolver(IPAddress.Loopback),
                CancellationToken.None));
    }

    [Fact]
    public async Task FetcherStoresMetadataAndAssetsInLocalCache()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var handler = new RouteHandler(request =>
        {
            var uri = request.RequestUri!.AbsoluteUri;
            return uri switch
            {
                "https://example.com/post" => HtmlResponse(
                    """
                    <meta property="og:title" content="Stored title">
                    <meta property="og:description" content="Stored description">
                    <meta property="og:image" content="/cover.jpg">
                    <link rel="icon" href="/icon.png">
                    """),
                "https://example.com/cover.jpg" => ImageResponse(
                    [1, 2, 3],
                    "image/jpeg"),
                "https://example.com/icon.png" => ImageResponse(
                    [4, 5],
                    "image/png"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        using var fetcher = new LinkPreviewFetcher(
            paths,
            httpClient,
            new FixedResolver(IPAddress.Parse("93.184.216.34")));

        var result = await fetcher.FetchAsync(new LinkPreviewCandidate(
            Guid.NewGuid(),
            "https://example.com/post",
            "https://example.com/post"));

        Assert.Equal(LinkPreviewStatus.Available, result.Status);
        Assert.Equal("Stored title", result.PageTitle);
        Assert.Equal("Stored description", result.PageDescription);
        Assert.NotNull(result.SiteIconPath);
        Assert.NotNull(result.PreviewImagePath);
        Assert.True(File.Exists(Path.Combine(_root, result.SiteIconPath!)));
        Assert.True(File.Exists(Path.Combine(_root, result.PreviewImagePath!)));
        var cached = fetcher.FindCachedArtwork("https://example.com/post");
        Assert.NotNull(cached);
        Assert.False(cached.IsSiteIcon);
        Assert.Equal(result.PreviewImagePath, cached.RelativePath);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task FetcherReadsMetadataBeforeLargeHtmlLimit()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var html = string.Concat(
            "<html><head>",
            new string('x', 700_000),
            """
            <meta property="og:title" content="Large video page">
            <meta property="og:image" content="/video-cover.jpg">
            """,
            new string('y', 400_000),
            "</head></html>");
        var handler = new RouteHandler(request =>
            request.RequestUri!.AbsoluteUri switch
            {
                "https://video.example/watch?v=123" => HtmlResponse(html),
                "https://video.example/video-cover.jpg" => ImageResponse(
                    [1, 2, 3, 4],
                    "image/jpeg"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        using var httpClient = new HttpClient(handler);
        using var fetcher = new LinkPreviewFetcher(
            paths,
            httpClient,
            new FixedResolver(IPAddress.Parse("93.184.216.34")));

        var result = await fetcher.FetchAsync(new LinkPreviewCandidate(
            Guid.NewGuid(),
            "https://video.example/watch?v=123",
            "https://video.example/watch?v=123"));

        Assert.Equal(LinkPreviewStatus.Available, result.Status);
        Assert.Equal("Large video page", result.PageTitle);
        Assert.NotNull(result.PreviewImagePath);
        Assert.True(File.Exists(Path.Combine(_root, result.PreviewImagePath!)));
    }

    [Fact]
    public async Task FetcherUsesSiteIconWhenPagePreviewIsUnavailable()
    {
        var paths = SentoryDataPaths.ForRoot(_root);
        var handler = new RouteHandler(request =>
            request.RequestUri!.AbsoluteUri switch
            {
                "https://example.com/private" =>
                    new HttpResponseMessage(HttpStatusCode.Forbidden),
                "https://example.com/favicon.ico" => ImageResponse(
                    [7, 8, 9],
                    "image/x-icon"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        using var httpClient = new HttpClient(handler);
        using var fetcher = new LinkPreviewFetcher(
            paths,
            httpClient,
            new FixedResolver(IPAddress.Parse("93.184.216.34")));

        var result = await fetcher.FetchAsync(new LinkPreviewCandidate(
            Guid.NewGuid(),
            "https://example.com/private",
            "https://example.com/private"));

        Assert.Equal(LinkPreviewStatus.Available, result.Status);
        Assert.NotNull(result.SiteIconPath);
        Assert.Null(result.PreviewImagePath);
        Assert.True(File.Exists(Path.Combine(_root, result.SiteIconPath!)));
        var cached = fetcher.FindCachedArtwork(
            "https://example.com/private");
        Assert.NotNull(cached);
        Assert.True(cached.IsSiteIcon);
        Assert.Equal(result.SiteIconPath, cached.RelativePath);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task FetcherDoesNotFollowRedirectToPrivateAddress()
    {
        var handler = new RouteHandler(_ => new HttpResponseMessage(
            HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("http://127.0.0.1/private") }
        });
        using var httpClient = new HttpClient(handler);
        using var fetcher = new LinkPreviewFetcher(
            SentoryDataPaths.ForRoot(_root),
            httpClient,
            new FixedResolver(IPAddress.Parse("93.184.216.34")));

        var result = await fetcher.FetchAsync(new LinkPreviewCandidate(
            Guid.NewGuid(),
            "https://example.com/start",
            "https://example.com/start"));

        Assert.Equal(LinkPreviewStatus.Unavailable, result.Status);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EnrichmentServiceUpdatesStoredLinkPreview()
    {
        const string url = "https://example.com/article";
        var paths = SentoryDataPaths.ForRoot(_root);
        var repository = new SqliteCaptureRepository(paths);
        await repository.InitializeAsync();
        Assert.True(UrlNormalizer.TryNormalize(url, out var normalized));
        await repository.UpsertUrlAsync(new UrlCaptureRequest(
            Guid.NewGuid(),
            url,
            normalized,
            SourceApp.KakaoTalk,
            CaptureMethod.KakaoCtrlVUrl,
            DeliveryStatus.NotObserved,
            "preview-test",
            DateTimeOffset.UtcNow,
            ["test"]));

        var handler = new RouteHandler(request =>
            request.RequestUri!.AbsoluteUri switch
            {
                url => HtmlResponse(
                    """
                    <meta property="og:title" content="Enriched title">
                    <meta name="description" content="Enriched description">
                    <meta property="og:image" content="/cover.jpg">
                    """),
                "https://example.com/cover.jpg" => ImageResponse(
                    [9, 8, 7],
                    "image/jpeg"),
                "https://example.com/favicon.ico" => ImageResponse(
                    [6, 5],
                    "image/x-icon"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        using var httpClient = new HttpClient(handler);
        using var fetcher = new LinkPreviewFetcher(
            paths,
            httpClient,
            new FixedResolver(IPAddress.Parse("93.184.216.34")));
        var service = new LinkPreviewEnrichmentService(repository, fetcher);

        var updated = await service.EnrichBatchAsync(
            4,
            DateTimeOffset.UtcNow.AddDays(-30));
        var item = Assert.Single(await repository.GetRecentAsync(10));

        Assert.Equal(1, updated);
        Assert.Equal(LinkPreviewStatus.Available, item.PreviewStatus);
        Assert.Equal("Enriched title", item.PageTitle);
        Assert.Equal("Enriched description", item.PageDescription);
        Assert.NotNull(item.SiteIconPath);
        Assert.NotNull(item.PreviewImagePath);
    }

    private static HttpResponseMessage HtmlResponse(string html)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, System.Text.Encoding.UTF8)
        };
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("text/html")
            {
                CharSet = "utf-8"
            };
        return response;
    }

    private static HttpResponseMessage ImageResponse(
        byte[] bytes,
        string mediaType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

    private sealed class FixedResolver(params IPAddress[] addresses)
        : IHostAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(
            string host,
            CancellationToken cancellationToken) =>
            Task.FromResult(addresses);
    }

    private sealed class RouteHandler(
        Func<HttpRequestMessage, HttpResponseMessage> route)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = route(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
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
