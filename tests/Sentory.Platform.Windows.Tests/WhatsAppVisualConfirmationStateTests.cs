using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class WhatsAppVisualConfirmationStateTests
{
    private static readonly WindowBounds Bounds = new(0, 0, 1200, 900);

    [Fact]
    public void ConfirmsOnlyAfterDraftSendInputAndOutgoingVisualChange()
    {
        var baseline = Frame(send: false, outgoingChanged: false);
        var state = new WhatsAppVisualConfirmationState(
            baseline,
            TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(
            WhatsAppVisualDecision.Pending,
            state.Observe(
                Frame(send: true, outgoingChanged: false),
                false,
                now));
        Assert.True(state.DraftObserved);
        Assert.Equal(
            WhatsAppVisualDecision.Pending,
            state.Observe(
                Frame(send: false, outgoingChanged: true),
                true,
                now.AddSeconds(1)));
        Assert.Equal(
            WhatsAppVisualDecision.Confirmed,
            state.Observe(
                Frame(send: false, outgoingChanged: true),
                true,
                now.AddSeconds(1.2)));
    }

    [Fact]
    public void DoesNotConfirmVisualChangeWithoutExplicitSendInput()
    {
        var state = new WhatsAppVisualConfirmationState(
            Frame(send: false, outgoingChanged: false),
            TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;

        state.Observe(
            Frame(send: true, outgoingChanged: false),
            false,
            now);

        Assert.Equal(
            WhatsAppVisualDecision.Pending,
            state.Observe(
                Frame(send: false, outgoingChanged: true),
                false,
                now.AddSeconds(1)));
    }

    [Fact]
    public void ConfirmsRapidPasteAndEnterWhenDraftFrameWasMissed()
    {
        var state = new WhatsAppVisualConfirmationState(
            Frame(send: false, outgoingChanged: false),
            TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(
            WhatsAppVisualDecision.Pending,
            state.Observe(
                Frame(send: false, outgoingChanged: true),
                true,
                now));
        Assert.False(state.DraftObserved);
        Assert.Equal(
            WhatsAppVisualDecision.Confirmed,
            state.Observe(
                Frame(send: false, outgoingChanged: true),
                true,
                now.AddMilliseconds(200)));
    }

    [Fact]
    public void DoesNotConfirmMissedDraftWithoutExplicitSendInput()
    {
        var state = new WhatsAppVisualConfirmationState(
            Frame(send: false, outgoingChanged: false),
            TimeSpan.FromSeconds(10));

        Assert.Equal(
            WhatsAppVisualDecision.Pending,
            state.Observe(
                Frame(send: false, outgoingChanged: true),
                false,
                DateTimeOffset.UtcNow));
        Assert.False(state.DraftObserved);
    }

    [Fact]
    public void CancelsRemovedDraftWithoutSendOrOutgoingChange()
    {
        var state = new WhatsAppVisualConfirmationState(
            Frame(send: false, outgoingChanged: false),
            TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;

        state.Observe(
            Frame(send: true, outgoingChanged: false),
            false,
            now);
        Assert.Equal(
            WhatsAppVisualDecision.Pending,
            state.Observe(
                Frame(send: false, outgoingChanged: false),
                false,
                now.AddSeconds(1)));
        Assert.Equal(
            WhatsAppVisualDecision.Cancelled,
            state.Observe(
                Frame(send: false, outgoingChanged: false),
                false,
                now.AddSeconds(11)));
    }

    [Fact]
    public void CancelsRemovedDraftDespiteVisualNoiseWithoutSendInput()
    {
        var state = new WhatsAppVisualConfirmationState(
            Frame(send: false, outgoingChanged: false),
            TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;

        state.Observe(
            Frame(send: true, outgoingChanged: false),
            false,
            now);
        state.Observe(
            Frame(send: false, outgoingChanged: true),
            false,
            now.AddSeconds(1));

        Assert.Equal(
            WhatsAppVisualDecision.Cancelled,
            state.Observe(
                Frame(send: false, outgoingChanged: true),
                false,
                now.AddSeconds(11)));
    }

    [Fact]
    public void IgnoresBottomRegionChangesWithoutGreenSendAffordance()
    {
        var baseline = Frame(send: false, outgoingChanged: false);
        var state = new WhatsAppVisualConfirmationState(
            baseline,
            TimeSpan.FromSeconds(10));

        state.Observe(
            baseline with
            {
                DraftPixels = ChangedPixels(100)
            },
            false,
            DateTimeOffset.UtcNow);

        Assert.False(state.DraftObserved);
    }

    private static WhatsAppVisualFrame Frame(
        bool send,
        bool outgoingChanged) =>
        new(
            Bounds,
            PlainPixels(100),
            outgoingChanged ? ChangedPixels(100) : PlainPixels(100),
            send);

    private static IReadOnlyList<int> PlainPixels(int count) =>
        Enumerable.Repeat(unchecked((int)0xFF202020), count).ToArray();

    private static IReadOnlyList<int> ChangedPixels(int count) =>
        Enumerable.Range(0, count)
            .Select(index => index < 10
                ? unchecked((int)0xFF21C063)
                : unchecked((int)0xFF202020))
            .ToArray();
}
