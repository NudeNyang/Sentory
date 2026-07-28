using System.Windows.Media;

namespace Sentory.App;

public static class GalleryArtworkDecodePolicy
{
    public const int CardWidth = 320;
    public const int SiteIconWidth = 64;
    public const int DetailWidth = 480;
}

public sealed class GalleryArtworkReference(
    Func<ImageSource?> loader)
{
    private readonly Func<ImageSource?> _loader =
        loader ?? throw new ArgumentNullException(nameof(loader));

    public ImageSource? Value => _loader();
}
