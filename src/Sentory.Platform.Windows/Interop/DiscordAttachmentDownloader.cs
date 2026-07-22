using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Interop;

public sealed class DiscordAttachmentDownloader : IDisposable
{
    internal const long MaximumImageBytes =
        ClipboardImageCodec.MaximumEncodedImageBytes;
    internal const int MaximumImagesPerBatch =
        ClipboardImageCodec.MaximumImagesPerBatch;

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
        long totalBytes = 0;
        foreach (var url in attachmentUrls
                     .Where(DiscordAttachmentUrlExtractor.IsAllowedAttachmentUrl)
                     .Distinct(StringComparer.Ordinal)
                     .Take(MaximumImagesPerBatch))
        {
            var remainingBytes =
                ClipboardImageCodec.MaximumBatchImageBytes - totalBytes;
            if (remainingBytes <= 0)
            {
                break;
            }

            var image = await TryDownloadAsync(
                url,
                Math.Min(MaximumImageBytes, remainingBytes),
                cancellationToken);
            if (image is not null && results.All(existing =>
                    !string.Equals(
                        existing.Sha256,
                        image.Sha256,
                        StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(image);
                totalBytes += image.ContentBytes.LongLength;
            }
        }

        return results;
    }

    private async Task<ClipboardImageSnapshot?> TryDownloadAsync(
        string url,
        long maximumBytes,
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
                response.Content.Headers.ContentLength is > 0 and var contentLength &&
                contentLength > maximumBytes)
            {
                return null;
            }

            var mimeType = response.Content.Headers.ContentType?.MediaType;
            if (mimeType is null ||
                !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var bytes = await ReadLimitedBytesAsync(
                response.Content,
                maximumBytes,
                cancellationToken);
            if (bytes is null)
            {
                return null;
            }

            var extension = Path.GetExtension(
                Uri.UnescapeDataString(finalUri.AbsolutePath));
            return ClipboardImageCodec.TryDecode(
                bytes,
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

    private static async Task<byte[]?> ReadLimitedBytesAsync(
        HttpContent content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        if (content.Headers.ContentLength is >= 0 and var contentLength)
        {
            if (contentLength > maximumBytes || contentLength > int.MaxValue)
            {
                return null;
            }

            var bytes = GC.AllocateUninitializedArray<byte>((int)contentLength);
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await stream.ReadAsync(
                    bytes.AsMemory(offset),
                    cancellationToken);
                if (read == 0)
                {
                    return null;
                }

                offset += read;
            }

            var overflowProbe = new byte[1];
            return await stream.ReadAsync(overflowProbe, cancellationToken) == 0
                ? bytes
                : null;
        }

        using var buffered = new MemoryStream();
        var buffer = new byte[81_920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return buffered.ToArray();
            }

            if (buffered.Length + read > maximumBytes)
            {
                return null;
            }

            await buffered.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
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
