using System.Net;
using System.Net.Http.Headers;
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
        Assert.Equal(3, handler.RequestCount);
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
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
