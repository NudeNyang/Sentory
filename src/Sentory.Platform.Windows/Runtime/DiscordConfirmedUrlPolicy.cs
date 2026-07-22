using Sentory.Core;

namespace Sentory.Platform.Windows.Runtime;

internal static class DiscordConfirmedUrlPolicy
{
    public static IReadOnlyList<NormalizedUrl> SelectForCapture(
        IReadOnlyList<NormalizedUrl> candidates,
        IReadOnlyList<string>? confirmedUrls)
    {
        if (confirmedUrls is null)
        {
            return candidates;
        }

        var confirmed = confirmedUrls.ToHashSet(StringComparer.Ordinal);
        return candidates
            .Where(candidate => confirmed.Contains(candidate.Value))
            .ToArray();
    }
}
