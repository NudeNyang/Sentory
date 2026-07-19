using Sentory.Core;

namespace Sentory.Core.Tests;

public sealed class ScrollIndicatorMetricsTests
{
    [Fact]
    public void CalculatesProportionalThumbSizeAndPosition()
    {
        var metrics = ScrollIndicatorMetrics.Calculate(
            trackHeight: 800,
            extentHeight: 2400,
            viewportHeight: 800,
            verticalOffset: 400);

        Assert.True(metrics.IsScrollable);
        Assert.Equal(267, metrics.ThumbHeight);
        Assert.Equal(533, metrics.ThumbTravel);
        Assert.Equal(133, metrics.ThumbTop);
    }

    [Fact]
    public void KeepsThumbAtDashboardMinimumSize()
    {
        var metrics = ScrollIndicatorMetrics.Calculate(
            trackHeight: 800,
            extentHeight: 100000,
            viewportHeight: 500,
            verticalOffset: 5000);

        Assert.True(metrics.IsScrollable);
        Assert.Equal(32, metrics.ThumbHeight);
        Assert.Equal(768, metrics.ThumbTravel);
        Assert.Equal(39, metrics.ThumbTop);
    }

    [Fact]
    public void HidesIndicatorWhenContentDoesNotOverflow()
    {
        var metrics = ScrollIndicatorMetrics.Calculate(
            trackHeight: 800,
            extentHeight: 600,
            viewportHeight: 800,
            verticalOffset: 0);

        Assert.False(metrics.IsScrollable);
        Assert.Equal(800, metrics.ThumbHeight);
        Assert.Equal(0, metrics.ThumbTravel);
        Assert.Equal(0, metrics.ThumbTop);
    }

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(5000, 533)]
    public void ClampsThumbPositionToTrack(
        double verticalOffset,
        double expectedTop)
    {
        var metrics = ScrollIndicatorMetrics.Calculate(
            trackHeight: 800,
            extentHeight: 2400,
            viewportHeight: 800,
            verticalOffset: verticalOffset);

        Assert.Equal(expectedTop, metrics.ThumbTop);
    }
}
