using Sentory.Core;

namespace Sentory.Infrastructure.Links;

public sealed class LinkPreviewEnrichmentService(
    ICaptureRepository repository,
    LinkPreviewFetcher fetcher)
{
    public async Task<int> EnrichBatchAsync(
        int limit,
        DateTimeOffset retryBefore,
        CancellationToken cancellationToken = default)
    {
        var candidates = await repository.GetLinkPreviewCandidatesAsync(
            limit,
            retryBefore,
            cancellationToken);
        var updated = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preview = await fetcher.FetchAsync(candidate, cancellationToken);
            if (await repository.UpdateLinkPreviewAsync(
                    candidate.ItemId,
                    preview,
                    cancellationToken))
            {
                updated++;
            }
        }

        return updated;
    }
}
