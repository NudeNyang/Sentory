using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class LinePreDropBaselinePolicyTests
{
    [Fact]
    public void UsesCompletedBaselineWithoutWaiting()
    {
        var snapshot = new LineAccessibilitySnapshot(
            string.Empty,
            new HashSet<string>(["one"]));

        Assert.Same(
            snapshot,
            LinePreDropBaselinePolicy.TryGetCompleted(
                Task.FromResult<LineAccessibilitySnapshot?>(snapshot)));
    }

    [Fact]
    public void DoesNotDelayRegistrationForPendingBaseline()
    {
        var pending = new TaskCompletionSource<LineAccessibilitySnapshot?>();

        Assert.Null(LinePreDropBaselinePolicy.TryGetCompleted(pending.Task));
        Assert.False(pending.Task.IsCompleted);
    }
}
