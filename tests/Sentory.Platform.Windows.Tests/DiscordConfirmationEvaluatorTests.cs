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
    [InlineData(DiscordConfirmationContentKind.Image, true, "message-list-unavailable", true)]
    [InlineData(DiscordConfirmationContentKind.Url, true, "renderer-accessibility-root-unavailable", true)]
    [InlineData(DiscordConfirmationContentKind.AttachmentDiscovery, true, "message-list-unavailable", true)]
    [InlineData(DiscordConfirmationContentKind.Warmup, false, "message-list-unavailable", false)]
    [InlineData(DiscordConfirmationContentKind.Image, false, "message-list-unavailable", false)]
    [InlineData(DiscordConfirmationContentKind.Image, true, "url-input-candidate-count:0", false)]
    public void RetriesOnlyTransientTargetFailuresAfterExplicitSend(
        DiscordConfirmationContentKind contentKind,
        bool explicitSendObserved,
        string unavailableSignal,
        bool expected)
    {
        var request = new DiscordConfirmationRequest(
            1,
            2,
            3,
            contentKind,
            [],
            ExplicitSendObserved: explicitSendObserved);

        Assert.Equal(
            expected,
            DiscordAccessibilityWorker.ShouldRetryTargetResolution(
                request,
                unavailableSignal));
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
    public void InvalidatesTargetCacheWhenDiscordChannelTitleChanges()
    {
        var request = new DiscordConfirmationRequest(
            10,
            20,
            30,
            DiscordConfirmationContentKind.Warmup,
            []);

        Assert.False(DiscordAccessibilityWorker.IsCacheContextMatch(
            request,
            10,
            20,
            30,
            "#이전 채널 | Discord",
            "#현재 채널 | Discord"));
    }

    [Fact]
    public void ReusesTargetCacheOnlyForSameDiscordWindowContext()
    {
        var request = new DiscordConfirmationRequest(
            10,
            20,
            30,
            DiscordConfirmationContentKind.Warmup,
            []);

        Assert.True(DiscordAccessibilityWorker.IsCacheContextMatch(
            request,
            10,
            20,
            30,
            "#작업장 | Discord",
            "#작업장 | Discord"));
        Assert.False(DiscordAccessibilityWorker.IsCacheContextMatch(
            request,
            10,
            99,
            30,
            "#작업장 | Discord",
            "#작업장 | Discord"));
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

    [Theory]
    [InlineData("첨부 파일 제거")]
    [InlineData("Remove attachment")]
    [InlineData("添付ファイルを削除")]
    [InlineData("移除附件")]
    public void RecognizesDraftAttachmentRemoveControls(string value)
    {
        Assert.True(
            DiscordAccessibilityWorker
                .LooksLikeDraftAttachmentRemoveControl(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("첨부 파일 수정")]
    [InlineData("Edit attachment")]
    public void RejectsSentAttachmentControlsAsDraftControls(string? value)
    {
        Assert.False(
            DiscordAccessibilityWorker
                .LooksLikeDraftAttachmentRemoveControl(value));
    }

    [Theory]
    [InlineData(0, 0, 1080, 1874, true)]
    [InlineData(0, 0, 1080, 80, false)]
    [InlineData(0, 100, 260, 1700, false)]
    [InlineData(300, 500, 780, 120, true)]
    [InlineData(300, 1500, 780, 300, true)]
    [InlineData(0, 0, 0, 0, true)]
    public void LimitsDraftInspectionToChatComposerRegion(
        int nodeLeft,
        int nodeTop,
        int nodeWidth,
        int nodeHeight,
        bool expected)
    {
        Assert.Equal(
            expected,
            DiscordAccessibilityWorker.IntersectsDraftInspectionRegion(
                0,
                0,
                1080,
                1874,
                nodeLeft,
                nodeTop,
                nodeWidth,
                nodeHeight));
    }
}
