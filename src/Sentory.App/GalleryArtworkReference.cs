using System.Windows.Media;

namespace Sentory.App;

public static class GalleryArtworkDecodePolicy
{
    public const int CardWidth = 384;
    public const int SiteIconWidth = 64;
    public const int DetailWidth = 480;
}

public sealed class GalleryArtworkReference
{
    private readonly Lazy<ImageSource?> _image;

    public GalleryArtworkReference(Func<ImageSource?> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _image = new Lazy<ImageSource?>(
            loader,
            LazyThreadSafetyMode.None);
    }

    public ImageSource? Value => _image.Value;
}
