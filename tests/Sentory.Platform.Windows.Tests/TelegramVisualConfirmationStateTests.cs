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

    [Fact]
    public void DropReleaseAndPreviewChangeDoNotConfirmWithoutSendInput()
    {
        var state = new TelegramVisualConfirmationState(Frame(changed: false));

        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(changed: true), false));
        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(changed: true), false));
        Assert.False(state.SendObserved);
    }

    [Fact]
    public void NativeDropConfirmsAfterStablePreviewIsDismissed()
    {
        var state = new TelegramVisualConfirmationState(
            Frame(variant: 1),
            Frame(variant: 0));

        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(variant: 1), false));
        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(variant: 1), false));
        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(variant: 2), false));
        Assert.Equal(
            TelegramVisualDecision.Confirmed,
            state.Observe(Frame(variant: 2), false));
        Assert.False(state.SendObserved);
    }

    [Fact]
    public void NativeDropDoesNotConfirmWhilePreviewRemainsOpen()
    {
        var state = new TelegramVisualConfirmationState(
            Frame(variant: 1),
            Frame(variant: 0));

        for (var index = 0; index < 8; index++)
        {
            Assert.Equal(
                TelegramVisualDecision.Pending,
                state.Observe(Frame(variant: 1), false));
        }

        Assert.False(state.SendObserved);
    }

    [Fact]
    public void NativeDropWaitsForPreviewToStabilizeBeforeWatchingForDismissal()
    {
        var state = new TelegramVisualConfirmationState(
            Frame(variant: 1),
            Frame(variant: 0));

        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(variant: 2), false));
        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(variant: 2), false));
        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(variant: 1), false));
    }

    [Fact]
    public void NativeDropCancellationReturningToPreDropFrameDoesNotConfirm()
    {
        var state = new TelegramVisualConfirmationState(
            Frame(variant: 1),
            Frame(variant: 0));

        state.Observe(Frame(variant: 1), false);
        state.Observe(Frame(variant: 1), false);

        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(variant: 0), false));
        Assert.Equal(
            TelegramVisualDecision.Pending,
            state.Observe(Frame(variant: 0), false));
    }

    private static TelegramVisualFrame Frame(bool changed) =>
        new(
            Bounds,
            changed ? ChangedPixels(100) : PlainPixels(100));

    private static TelegramVisualFrame Frame(int variant) =>
        new(
            Bounds,
            variant switch
            {
                0 => PlainPixels(100),
                1 => ChangedPixels(100),
                _ => AlternateChangedPixels(100)
            });

    private static IReadOnlyList<int> PlainPixels(int count) =>
        Enumerable.Repeat(unchecked((int)0xFF202020), count).ToArray();

    private static IReadOnlyList<int> ChangedPixels(int count) =>
        Enumerable.Range(0, count)
            .Select(index => index < 10
                ? unchecked((int)0xFF21A4F4)
                : unchecked((int)0xFF202020))
            .ToArray();

    private static IReadOnlyList<int> AlternateChangedPixels(int count) =>
        Enumerable.Range(0, count)
            .Select(index => index < 20
                ? unchecked((int)0xFFE04F5F)
                : unchecked((int)0xFF202020))
            .ToArray();
}
