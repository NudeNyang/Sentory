using System.Windows.Media;

namespace Sentory.App.Tests;

public sealed class GalleryArtworkReferenceTests
{
    [Fact]
    public void DefersDetailImageLoadingUntilRequested()
    {
        var loads = 0;
        ImageSource expected = new DrawingImage();
        var reference = new GalleryArtworkReference(() =>
        {
            loads++;
            return expected;
        });

        Assert.Equal(0, loads);
        Assert.Same(expected, reference.Value);
        Assert.Equal(1, loads);
    }

    [Fact]
    public void UsesDisplaySizedDecodeWidths()
    {
        Assert.Equal(384, GalleryArtworkDecodePolicy.CardWidth);
        Assert.Equal(64, GalleryArtworkDecodePolicy.SiteIconWidth);
        Assert.Equal(480, GalleryArtworkDecodePolicy.DetailWidth);
    }
}
