namespace Sentory.Platform.Windows.Runtime;

internal readonly record struct DiscordPendingImageCandidate(
    Guid EventId,
    DateTimeOffset PastedAt,
    int ImageCount);

internal static class DiscordDraftImageCandidatePolicy
{
    private static readonly TimeSpan PreviewAppearanceGrace =
        TimeSpan.FromMilliseconds(500);

    public static IReadOnlyList<Guid> SelectCandidatesToCancel(
        IReadOnlyList<DiscordPendingImageCandidate> candidates,
        int draftImageCount,
        DateTimeOffset observedAt)
    {
        var excessImages = candidates.Sum(candidate => candidate.ImageCount) -
                           Math.Max(0, draftImageCount);
        if (excessImages <= 0)
        {
            return [];
        }

        var cancelled = new List<Guid>();
        foreach (var candidate in candidates.OrderBy(candidate =>
                     candidate.PastedAt))
        {
            if (excessImages <= 0)
            {
                break;
            }

            if (candidate.ImageCount <= 0 ||
                observedAt - candidate.PastedAt < PreviewAppearanceGrace ||
                candidate.ImageCount > excessImages)
            {
                continue;
            }

            cancelled.Add(candidate.EventId);
            excessImages -= candidate.ImageCount;
        }

        return cancelled;
    }
}
