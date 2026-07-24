using Sentory.Core;

namespace Sentory.App;

internal static class AutoFavoritePolicy
{
    public static bool ShouldAdd(
        ContentKind kind,
        bool isFavorite,
        int copyCount,
        bool enabled,
        int threshold) =>
        enabled &&
        !isFavorite &&
        copyCount >= threshold &&
        kind is ContentKind.Url or ContentKind.Image;
}
