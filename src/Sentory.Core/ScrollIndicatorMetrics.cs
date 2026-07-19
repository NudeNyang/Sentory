namespace Sentory.Core;

public readonly record struct ScrollIndicatorMetrics(
    bool IsScrollable,
    double ThumbHeight,
    double ThumbTravel,
    double ThumbTop)
{
    public static ScrollIndicatorMetrics Calculate(
        double trackHeight,
        double extentHeight,
        double viewportHeight,
        double verticalOffset,
        double minimumThumbHeight = 32)
    {
        trackHeight = Normalize(trackHeight);
        extentHeight = Normalize(extentHeight);
        viewportHeight = Normalize(viewportHeight);
        minimumThumbHeight = Normalize(minimumThumbHeight);

        var scrollRange = extentHeight - viewportHeight;
        var isScrollable = trackHeight > 0 && scrollRange > 1;
        if (!isScrollable)
        {
            return new ScrollIndicatorMetrics(
                false,
                trackHeight,
                0,
                0);
        }

        var proportionalHeight = Round(
            viewportHeight / extentHeight * trackHeight);
        var thumbHeight = Math.Min(
            trackHeight,
            Math.Max(minimumThumbHeight, proportionalHeight));
        var thumbTravel = Math.Max(trackHeight - thumbHeight, 0);
        var clampedOffset = Math.Clamp(verticalOffset, 0, scrollRange);
        var thumbTop = thumbTravel > 0
            ? Round(clampedOffset / scrollRange * thumbTravel)
            : 0;

        return new ScrollIndicatorMetrics(
            true,
            thumbHeight,
            thumbTravel,
            thumbTop);
    }

    private static double Normalize(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;

    private static double Round(double value) =>
        Math.Round(value, MidpointRounding.AwayFromZero);
}
