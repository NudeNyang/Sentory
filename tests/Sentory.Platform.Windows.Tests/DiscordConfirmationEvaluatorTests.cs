using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordConfirmationEvaluatorTests
{
    [Fact]
    public void ConfirmsOnlyClearedInputAndMatchingNewMessage()
    {
        var result = DiscordConfirmationEvaluator.Evaluate(
            21,
            new DiscordCandidateObservation(
                true,
                false,
                true,
                22,
                true));

        Assert.Equal(DiscordCandidateDecision.Confirmed, result);
    }

    [Fact]
    public void KeepsPasteOnlyCandidatePending()
    {
        var result = DiscordConfirmationEvaluator.Evaluate(
            21,
            new DiscordCandidateObservation(
                true,
                true,
                false,
                21,
                false));

        Assert.Equal(DiscordCandidateDecision.Pending, result);
    }

    [Fact]
    public void KeepsShiftEnterCandidatePending()
    {
        var result = DiscordConfirmationEvaluator.Evaluate(
            21,
            new DiscordCandidateObservation(
                true,
                true,
                false,
                21,
                false));

        Assert.Equal(DiscordCandidateDecision.Pending, result);
    }

    [Fact]
    public void RejectsEditedOrDeletedUrl()
    {
        var result = DiscordConfirmationEvaluator.Evaluate(
            21,
            new DiscordCandidateObservation(
                true,
                false,
                false,
                21,
                false));

        Assert.Equal(DiscordCandidateDecision.Cancelled, result);
    }

    [Fact]
    public void DoesNotConfirmUnmatchedNewMessage()
    {
        var result = DiscordConfirmationEvaluator.Evaluate(
            21,
            new DiscordCandidateObservation(
                true,
                false,
                true,
                22,
                false));

        Assert.Equal(DiscordCandidateDecision.Pending, result);
    }
}
