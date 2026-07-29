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

    [Fact]
    public void MarksArtworkThatShouldBeLoadedAwayFromTheUiThread()
    {
        var reference = new GalleryArtworkReference(
            () => null,
            preferBackgroundLoad: true);

        Assert.True(reference.PreferBackgroundLoad);
        Assert.False(reference.IsValueCreated);
    }

    [Fact]
    public async Task SerializesBackgroundArtworkDecoding()
    {
        var activeLoads = 0;
        var maximumActiveLoads = 0;
        var references = Enumerable.Range(0, 3).Select(_ =>
            new GalleryArtworkReference(
                () =>
                {
                    var active = Interlocked.Increment(ref activeLoads);
                    InterlockedExtensions.Max(
                        ref maximumActiveLoads,
                        active);
                    Thread.Sleep(40);
                    Interlocked.Decrement(ref activeLoads);
                    return null;
                },
                preferBackgroundLoad: true)).ToArray();

        await Task.WhenAll(references.Select(reference =>
            reference.LoadAsync()));

        Assert.Equal(1, maximumActiveLoads);
    }
}

file static class InterlockedExtensions
{
    public static void Max(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(
                ref target,
                value,
                current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
