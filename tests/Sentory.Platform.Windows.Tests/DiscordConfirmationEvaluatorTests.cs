using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordConfirmationEvaluatorTests
{
    [Fact]
    public void DoesNotRequirePopulatedInputAfterSendKeyWasObserved()
    {
        var request = new DiscordConfirmationRequest(
            1,
            2,
            3,
            DiscordConfirmationContentKind.Url,
            ["https://example.com/"],
            ExplicitSendObserved: true);

        Assert.False(
            DiscordAccessibilityWorker.RequiresMatchingUrlInput(request));
    }

    [Fact]
    public void RequiresPopulatedInputBeforeSendKeyWasObserved()
    {
        var request = new DiscordConfirmationRequest(
            1,
            2,
            3,
            DiscordConfirmationContentKind.Url,
            ["https://example.com/"]);

        Assert.True(
            DiscordAccessibilityWorker.RequiresMatchingUrlInput(request));
    }

    [Theory]
    [InlineData(false, 1, 0, true)]
    [InlineData(true, 1, 0, false)]
    [InlineData(true, 1, 1, true)]
    [InlineData(false, 0, 1, false)]
    public void CompletesTargetSearchAsSoonAsRequiredTargetsExist(
        bool requireMatchingInput,
        int messageListCount,
        int inputCandidateCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            DiscordAccessibilityWorker.IsTargetSearchComplete(
                requireMatchingInput,
                messageListCount,
                inputCandidateCount));
    }

    [Fact]
    public void ConfirmsOnlyClearedInputAndMatchingNewMessage()
    {
        var result = DiscordConfirmationEvaluator.Evaluate(
            21,
            new DiscordCandidateObservation(
                true,
                false,
                true,
                1,
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
                0,
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
                0,
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
                0,
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
                1,
                22,
                false));

        Assert.Equal(DiscordCandidateDecision.Pending, result);
    }

    [Fact]
    public void ConfirmsImageOnlyForMatchingOwnedNewMessage()
    {
        var result = DiscordImageConfirmationEvaluator.Evaluate(
            21,
            new DiscordImageCandidateObservation(
                true,
                1,
                22,
                true));

        Assert.Equal(DiscordCandidateDecision.Confirmed, result);
    }

    [Fact]
    public void KeepsImagePendingWhenAnotherNewMessageArrivesFirst()
    {
        var result = DiscordImageConfirmationEvaluator.Evaluate(
            21,
            new DiscordImageCandidateObservation(
                true,
                1,
                22,
                false));

        Assert.Equal(DiscordCandidateDecision.Pending, result);
    }

    [Fact]
    public void CancelsImageWhenDiscordContextChanges()
    {
        var result = DiscordImageConfirmationEvaluator.Evaluate(
            21,
            new DiscordImageCandidateObservation(
                false,
                0,
                21,
                false));

        Assert.Equal(DiscordCandidateDecision.Cancelled, result);
    }

    [Fact]
    public void ConfirmsUrlWhenLatestMessageChangesWithoutCountIncrease()
    {
        var result = DiscordConfirmationEvaluator.Evaluate(
            22,
            new DiscordCandidateObservation(
                true,
                false,
                true,
                1,
                22,
                true));

        Assert.Equal(DiscordCandidateDecision.Confirmed, result);
    }

    [Fact]
    public void ConfirmsImageWhenLatestMessageChangesWithoutCountIncrease()
    {
        var result = DiscordImageConfirmationEvaluator.Evaluate(
            22,
            new DiscordImageCandidateObservation(
                true,
                1,
                22,
                true));

        Assert.Equal(DiscordCandidateDecision.Confirmed, result);
    }

    [Theory]
    [InlineData("Image")]
    [InlineData("클립보드 이미지")]
    [InlineData("attachment.png")]
    [InlineData("https://cdn.discordapp.com/attachments/1/2/image.png")]
    public void RecognizesImageAttachmentDescriptors(string value)
    {
        Assert.True(
            DiscordAccessibilityWorker.LooksLikeImageDescriptor(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("사용자 아바타")]
    [InlineData("일반 메시지")]
    public void RejectsUnrelatedImageDescriptors(string? value)
    {
        Assert.False(
            DiscordAccessibilityWorker.LooksLikeImageDescriptor(value));
    }

    [Theory]
    [InlineData("첨부 파일 수정")]
    [InlineData("Edit attachment")]
    [InlineData("EDIT ATTACHMENT")]
    public void RecognizesOwnedImageAttachmentControls(string value)
    {
        Assert.True(
            DiscordAccessibilityWorker.LooksLikeOwnedAttachmentControl(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("첨부 파일 제거")]
    [InlineData("이미지")]
    public void RejectsUnrelatedAttachmentControls(string? value)
    {
        Assert.False(
            DiscordAccessibilityWorker.LooksLikeOwnedAttachmentControl(value));
    }
}
