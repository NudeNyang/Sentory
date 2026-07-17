namespace Sentory.Core;

public sealed class CaptureCoordinator(ICaptureRepository repository)
{
    public Task<CaptureResult> CaptureImageAsync(
        Guid eventId,
        ReadOnlyMemory<byte> pngBytes,
        string sha256,
        int pixelWidth,
        int pixelHeight,
        SourceApp sourceApp,
        CaptureMethod captureMethod,
        DeliveryStatus deliveryStatus,
        string contextHash,
        DateTimeOffset capturedAt,
        IReadOnlyList<string> confirmationSignals,
        CancellationToken cancellationToken = default) =>
        repository.UpsertImageAsync(
            new ImageCaptureRequest(
                eventId,
                pngBytes,
                sha256,
                pixelWidth,
                pixelHeight,
                sourceApp,
                captureMethod,
                deliveryStatus,
                contextHash,
                capturedAt,
                confirmationSignals),
            cancellationToken);

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
