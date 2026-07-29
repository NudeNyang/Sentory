using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class LineNativeDropBaselineCacheTests
{
    private static readonly LineDropTarget Target = new(
        new nint(20),
        84,
        new WindowBounds(0, 0, 900, 700));
    private static readonly LineAccessibilitySnapshot Snapshot = new(
        string.Empty,
        new HashSet<string>(["one", "two"]));

    [Fact]
    public void ReturnsRecentBaselineForSameLineWindow()
    {
        var cache = new LineNativeDropBaselineCache(
            TimeSpan.FromSeconds(10));
        var capturedAt = DateTimeOffset.UtcNow;
        cache.Observe(Target, Snapshot, capturedAt);

        Assert.True(cache.TryGet(
            Target,
            capturedAt.AddSeconds(9),
            out var baseline,
            out var age));
        Assert.Same(Snapshot, baseline);
        Assert.Equal(TimeSpan.FromSeconds(9), age);
    }

    [Fact]
    public void RejectsExpiredOrDifferentWindowBaseline()
    {
        var cache = new LineNativeDropBaselineCache(
            TimeSpan.FromSeconds(10));
        var capturedAt = DateTimeOffset.UtcNow;
        cache.Observe(Target, Snapshot, capturedAt);

        Assert.False(cache.TryGet(
            Target,
            capturedAt.AddSeconds(11),
            out _,
            out _));
        Assert.False(cache.TryGet(
            Target with { MainWindow = new nint(21) },
            capturedAt.AddSeconds(1),
            out _,
            out _));
    }

    [Fact]
    public void ReturnsLastKnownBaselineAfterFreshWindowExpires()
    {
        var cache = new LineNativeDropBaselineCache(
            TimeSpan.FromSeconds(10));
        var capturedAt = DateTimeOffset.UtcNow;
        cache.Observe(Target, Snapshot, capturedAt);

        Assert.True(cache.TryGetLastKnown(
            Target,
            capturedAt.AddHours(5),
            out var baseline,
            out var age));
        Assert.Same(Snapshot, baseline);
        Assert.Equal(TimeSpan.FromHours(5), age);
    }

    [Fact]
    public void RejectsLastKnownBaselineFromDifferentLineProcess()
    {
        var cache = new LineNativeDropBaselineCache(
            TimeSpan.FromSeconds(10));
        var capturedAt = DateTimeOffset.UtcNow;
        cache.Observe(Target, Snapshot, capturedAt);

        Assert.False(cache.TryGetLastKnown(
            Target with { ProcessId = 85 },
            capturedAt.AddHours(5),
            out _,
            out _));
    }
}
