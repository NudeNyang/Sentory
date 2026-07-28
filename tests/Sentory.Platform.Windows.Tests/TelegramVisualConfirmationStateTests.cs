using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class TelegramVisualConfirmationStateTests
{
    private static readonly WindowBounds Bounds = new(0, 0, 1200, 900);

    [Fact]
    public void ConfirmsOnlyAfterExplicitSendAndTwoChangedFrames()
    {
        var state = new TelegramVisualConfirmationState(Frame(changed: false));

        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(changed: false), false));
        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(changed: true), true));
        Assert.Equal(
            TelegramVisualDecision.Confirmed,
            state.Observe(Frame(changed: true), true));
    }

    [Fact]
    public void RefreshesPreSendFrameWhileDraftChanges()
    {
        var state = new TelegramVisualConfirmationState(Frame(changed: false));
        state.Observe(Frame(changed: true), false);

        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(changed: true), true));
    }

    [Fact]
    public void DoesNotConfirmConversationNoiseWithoutSendInput()
    {
        var state = new TelegramVisualConfirmationState(Frame(changed: false));

        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(changed: true), false));
        Assert.False(state.SendObserved);
    }

    private static TelegramVisualFrame Frame(bool changed) =>
        new(
            Bounds,
            changed ? ChangedPixels(100) : PlainPixels(100));

    private static IReadOnlyList<int> PlainPixels(int count) =>
        Enumerable.Repeat(unchecked((int)0xFF202020), count).ToArray();

    private static IReadOnlyList<int> ChangedPixels(int count) =>
        Enumerable.Range(0, count)
            .Select(index => index < 10
                ? unchecked((int)0xFF21A4F4)
                : unchecked((int)0xFF202020))
            .ToArray();
}
