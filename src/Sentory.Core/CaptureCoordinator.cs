namespace Sentory.Core;

public sealed class CaptureCoordinator(ICaptureRepository repository)
{
    public Task<CaptureResult> CaptureImageAsync(
        Guid eventId,
        ReadOnlyMemory<byte> contentBytes,
        string sha256,
        int pixelWidth,
        int pixelHeight,
        string mimeType,
        string fileExtension,
        SourceApp sourceApp,
        CaptureMethod captureMethod,
        DeliveryStatus deliveryStatus,
        string contextHash,
        DateTimeOffset capturedAt,
        IReadOnlyList<string> confirmationSignals,
        string? originalFileName = null,
        CancellationToken cancellationToken = default) =>
        repository.UpsertImageAsync(
            new ImageCaptureRequest(
                eventId,
                contentBytes,
                sha256,
                pixelWidth,
                pixelHeight,
                mimeType,
                fileExtension,
                sourceApp,
                captureMethod,
                deliveryStatus,
                contextHash,
                capturedAt,
                confirmationSignals,
                originalFileName),
            cancellationToken);

    public async Task<CaptureResult?> CaptureBatchAsync(
        Guid eventId,
        string? clipboardText,
        IReadOnlyList<ImageCapturePayload> images,
        SourceApp sourceApp,
        CaptureMethod captureMethod,
        DeliveryStatus deliveryStatus,
        string contextHash,
        DateTimeOffset capturedAt,
        IReadOnlyList<string> confirmationSignals,
        CancellationToken cancellationToken = default)
    {
        var members = new List<CollectionMemberCaptureRequest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in UrlExtractor.Extract(clipboardText ?? string.Empty))
        {
            var key = $"url:{url.Value}";
            if (!seen.Add(key))
            {
                continue;
            }

            members.Add(new CollectionMemberCaptureRequest(
                ContentKind.Url,
                url.Original,
                url.Value,
                url.Domain,
                ReadOnlyMemory<byte>.Empty,
                null,
                0,
                0,
                null,
                null));
        }

        foreach (var image in images)
        {
            var normalizedHash = image.Sha256.ToLowerInvariant();
            var key = $"image:{normalizedHash}";
            if (!seen.Add(key))
            {
                continue;
            }

            members.Add(new CollectionMemberCaptureRequest(
                ContentKind.Image,
                image.OriginalFileName ?? string.Empty,
                $"sha256:{normalizedHash}",
                string.Empty,
                image.ContentBytes,
                normalizedHash,
                image.PixelWidth,
                image.PixelHeight,
                image.MimeType,
                image.FileExtension));
        }

        if (members.Count == 0)
        {
            return null;
        }

        if (members.Count == 1)
        {
            var member = members[0];
            if (member.Kind == ContentKind.Url)
            {
                return (await CaptureUrlsAsync(
                    eventId,
                    member.OriginalUrl,
                    sourceApp,
                    captureMethod,
                    deliveryStatus,
                    contextHash,
                    capturedAt,
                    confirmationSignals,
                    cancellationToken)).Single();
            }

            return await CaptureImageAsync(
                eventId,
                member.ContentBytes,
                member.Sha256!,
                member.PixelWidth,
                member.PixelHeight,
                member.MimeType!,
                member.FileExtension!,
                sourceApp,
                captureMethod,
                deliveryStatus,
                contextHash,
                capturedAt,
                confirmationSignals,
                member.OriginalUrl,
                cancellationToken);
        }

        var signature = CaptureCollectionIdentity.CreateSignature(members);
        return await repository.UpsertCollectionAsync(
            new CollectionCaptureRequest(
                eventId,
                signature,
                members,
                sourceApp,
                captureMethod,
                deliveryStatus,
                contextHash,
                capturedAt,
                confirmationSignals),
            cancellationToken);
    }

    public async Task<IReadOnlyList<CaptureResult>> CaptureUrlsAsync(
        Guid eventId,
        string clipboardText,
        SourceApp sourceApp,
        CaptureMethod captureMethod,
        DeliveryStatus deliveryStatus,
        string contextHash,
        DateTimeOffset capturedAt,
        IReadOnlyList<string> confirmationSignals,
        CancellationToken cancellationToken = default)
    {
        var urls = UrlExtractor.Extract(clipboardText);
        if (urls.Count == 0)
        {
            return [];
        }

        var results = new List<CaptureResult>(urls.Count);
        for (var index = 0; index < urls.Count; index++)
        {
            var urlEventId = DeriveEventId(eventId, index);
            var request = new UrlCaptureRequest(
                urlEventId,
                urls[index].Original,
                urls[index],
                sourceApp,
                captureMethod,
                deliveryStatus,
                contextHash,
                capturedAt,
                confirmationSignals);
            results.Add(await repository.UpsertUrlAsync(
                request,
                cancellationToken));
        }

        return results;
    }

    private static Guid DeriveEventId(Guid eventId, int index)
    {
        if (index == 0)
        {
            return eventId;
        }

        Span<byte> bytes = stackalloc byte[16];
        eventId.TryWriteBytes(bytes);
        var current = BitConverter.ToInt32(bytes[..4]);
        BitConverter.TryWriteBytes(bytes[..4], current ^ index);
        return new Guid(bytes);
    }
}
