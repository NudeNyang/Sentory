using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Interop;

public sealed class DiscordAttachmentDownloader : IDisposable
{
    internal const long MaximumImageBytes = 128L * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public DiscordAttachmentDownloader()
        : this(CreateClient(), ownsClient: true)
    {
    }

    internal DiscordAttachmentDownloader(
        HttpClient httpClient,
        bool ownsClient = false)
    {
        _httpClient = httpClient;
        _ownsClient = ownsClient;
    }

    public async Task<IReadOnlyList<ClipboardImageSnapshot>> DownloadAsync(
        IEnumerable<string> attachmentUrls,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ClipboardImageSnapshot>();
        foreach (var url in attachmentUrls
                     .Where(DiscordAttachmentUrlExtractor.IsAllowedAttachmentUrl)
                     .Distinct(StringComparer.Ordinal))
        {
            var image = await TryDownloadAsync(url, cancellationToken);
            if (image is not null && results.All(existing =>
                    !string.Equals(
                        existing.Sha256,
                        image.Sha256,
                        StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(image);
            }
        }

        return results;
    }

    private async Task<ClipboardImageSnapshot?> TryDownloadAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.RequestMessage?.RequestUri is not { } finalUri ||
                !DiscordAttachmentUrlExtractor.IsAllowedAttachmentUrl(
                    finalUri.AbsoluteUri) ||
                response.Content.Headers.ContentLength is > MaximumImageBytes)
            {
                return null;
            }

            var mimeType = response.Content.Headers.ContentType?.MediaType;
            if (mimeType is null ||
                !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var content = new MemoryStream();
            var buffer = new byte[81_920];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (content.Length + read > MaximumImageBytes)
                {
                    return null;
                }

                await content.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }

            var extension = Path.GetExtension(
                Uri.UnescapeDataString(finalUri.AbsolutePath));
            return ClipboardImageCodec.TryDecode(
                content.ToArray(),
                extension,
                mimeType);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or IOException or
                  TaskCanceledException or UriFormatException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3
        })
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Sentory", "0.9"));
        return client;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
