using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Sentory.Core;
using Sentory.Infrastructure.Data;

namespace Sentory.Infrastructure.Links;

public sealed class LinkPreviewFetcher : IDisposable
{
    private const int MaximumRedirects = 3;
    private const int MaximumHtmlBytes = 1024 * 1024;
    private const int MaximumAssetBytes = 2 * 1024 * 1024;
    private readonly SentoryDataPaths _paths;
    private readonly HttpClient _httpClient;
    private readonly IHostAddressResolver _resolver;
    private readonly bool _ownsHttpClient;

    public LinkPreviewFetcher(SentoryDataPaths paths)
        : this(paths, CreateHttpClient(), new DnsHostAddressResolver(), true)
    {
    }

    internal LinkPreviewFetcher(
        SentoryDataPaths paths,
        HttpClient httpClient,
        IHostAddressResolver resolver,
        bool ownsHttpClient = false)
    {
        _paths = paths;
        _httpClient = httpClient;
        _resolver = resolver;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<LinkPreviewUpdate> FetchAsync(
        LinkPreviewCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var fetchedAt = DateTimeOffset.UtcNow;
        try
        {
            if (!Uri.TryCreate(candidate.Url, UriKind.Absolute, out var pageUri))
            {
                return Unavailable(fetchedAt);
            }

            var key = CreateCacheKey(candidate.NormalizedKey);

            using var response = await SendWithRedirectsAsync(
                pageUri,
                "text/html,application/xhtml+xml",
                cancellationToken);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentType?.MediaType is not string mediaType ||
                !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                var fallbackIcon = await DownloadAssetAsync(
                    new Uri(pageUri, "/favicon.ico"),
                    key,
                    "icon",
                    allowGenericBinary: true,
                    cancellationToken);
                return fallbackIcon is null
                    ? Unavailable(fetchedAt)
                    : new LinkPreviewUpdate(
                        LinkPreviewStatus.Available,
                        null,
                        null,
                        fallbackIcon,
                        null,
                        fetchedAt);
            }

            var htmlBytes = await ReadLimitedAsync(
                response.Content,
                MaximumHtmlBytes,
                cancellationToken);
            var html = DecodeHtml(htmlBytes, response.Content.Headers.ContentType);
            var finalUri = response.RequestMessage?.RequestUri ?? pageUri;
            var parsed = LinkPreviewHtmlParser.Parse(html, finalUri);
            var previewPath = await DownloadAssetAsync(
                parsed.PreviewImageUri,
                key,
                "cover",
                allowGenericBinary: false,
                cancellationToken);
            var iconUri = parsed.SiteIconUri ?? new Uri(finalUri, "/favicon.ico");
            var iconPath = await DownloadAssetAsync(
                iconUri,
                key,
                "icon",
                allowGenericBinary: true,
                cancellationToken);
            var status = parsed.Title is not null ||
                         parsed.Description is not null ||
                         previewPath is not null ||
                         iconPath is not null
                ? LinkPreviewStatus.Available
                : LinkPreviewStatus.Unavailable;
            return new LinkPreviewUpdate(
                status,
                parsed.Title,
                parsed.Description,
                iconPath,
                previewPath,
                fetchedAt);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or
                  InvalidDataException or NotSupportedException or
                  UnauthorizedAccessException)
        {
            return Unavailable(fetchedAt);
        }
    }

    public CachedLinkPreviewArtwork? FindCachedArtwork(string normalizedKey)
    {
        if (string.IsNullOrWhiteSpace(normalizedKey) ||
            !Directory.Exists(_paths.LinkPreviewsDirectory))
        {
            return null;
        }

        try
        {
            var key = CreateCacheKey(normalizedKey);
            var cover = Directory.EnumerateFiles(
                    _paths.LinkPreviewsDirectory,
                    $"{key}-cover.*",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (cover is not null)
            {
                return new CachedLinkPreviewArtwork(
                    Path.Combine("link-previews", Path.GetFileName(cover)),
                    false);
            }

            var icon = Directory.EnumerateFiles(
                    _paths.LinkPreviewsDirectory,
                    $"{key}-icon.*",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            return icon is null
                ? null
                : new CachedLinkPreviewArtwork(
                    Path.Combine("link-previews", Path.GetFileName(icon)),
                    true);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string CreateCacheKey(string normalizedKey) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalizedKey)))
            .ToLowerInvariant();

    private async Task<string?> DownloadAssetAsync(
        Uri? uri,
        string key,
        string role,
        bool allowGenericBinary,
        CancellationToken cancellationToken)
    {
        if (uri is null)
        {
            return null;
        }

        try
        {
            using var response = await SendWithRedirectsAsync(
                uri,
                "image/avif,image/webp,image/png,image/jpeg,image/gif,image/x-icon,*/*;q=0.1",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null ||
                (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                 !(allowGenericBinary && string.Equals(
                     mediaType,
                     "application/octet-stream",
                     StringComparison.OrdinalIgnoreCase))))
            {
                return null;
            }

            var bytes = await ReadLimitedAsync(
                response.Content,
                MaximumAssetBytes,
                cancellationToken);
            if (bytes.Length == 0)
            {
                return null;
            }

            _paths.EnsureDirectories();
            var extension = GetExtension(mediaType, uri);
            var fileName = $"{key}-{role}{extension}";
            var absolutePath = Path.Combine(_paths.LinkPreviewsDirectory, fileName);
            var temporaryPath = $"{absolutePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(
                    temporaryPath,
                    bytes,
                    cancellationToken);
                File.Move(temporaryPath, absolutePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return Path.Combine("link-previews", fileName);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or
                  InvalidDataException or NotSupportedException or
                  UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendWithRedirectsAsync(
        Uri uri,
        string accept,
        CancellationToken cancellationToken)
    {
        var current = uri;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            if (!await LinkPreviewUriPolicy.IsAllowedAsync(
                    current,
                    _resolver,
                    cancellationToken))
            {
                throw new HttpRequestException("Blocked link preview address.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd(accept);
            request.Headers.UserAgent.ParseAdd("Sentory/1.0 LinkPreview");
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if ((int)response.StatusCode is < 300 or >= 400)
            {
                return response;
            }

            if (redirect == MaximumRedirects ||
                response.Headers.Location is not { } location)
            {
                response.Dispose();
                throw new HttpRequestException("Too many or invalid redirects.");
            }

            current = location.IsAbsoluteUri
                ? location
                : new Uri(current, location);
            response.Dispose();
        }

        throw new HttpRequestException("Redirect handling failed.");
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 and var contentLength &&
            contentLength > maximumBytes)
        {
            throw new InvalidDataException("Link preview response is too large.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > maximumBytes)
            {
                throw new InvalidDataException("Link preview response is too large.");
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return memory.ToArray();
    }

    private static string DecodeHtml(
        byte[] bytes,
        MediaTypeHeaderValue? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType?.CharSet))
        {
            try
            {
                return Encoding.GetEncoding(
                    contentType.CharSet.Trim('"')).GetString(bytes);
            }
            catch (ArgumentException)
            {
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string GetExtension(string mediaType, Uri uri) =>
        mediaType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/avif" => ".avif",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            _ => Path.GetExtension(uri.AbsolutePath) is { Length: > 0 and <= 5 } value
                ? value.ToLowerInvariant()
                : ".img"
        };

    private static LinkPreviewUpdate Unavailable(DateTimeOffset fetchedAt) =>
        new(
            LinkPreviewStatus.Unavailable,
            null,
            null,
            null,
            null,
            fetchedAt);

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli,
            UseCookies = false
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

public sealed record CachedLinkPreviewArtwork(
    string RelativePath,
    bool IsSiteIcon);
