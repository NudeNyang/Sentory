using Sentory.Core;
using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class WeChatConfirmationPolicyTests
{
    [Fact]
    public void NativeImageDropUsesConfirmedDropCaptureMethod()
    {
        var method = WeChatCaptureMethodPolicy.Select(
            hasImages: true,
            nativeDrop: true);

        Assert.Equal(CaptureMethod.WeChatConfirmedDrop, method);
    }

    [Fact]
    public void ImageMessageRequiresExplicitSendBeforeConfirmation()
    {
        Assert.False(WeChatNewMessageConfirmationPolicy.IsConfirmed(
            string.Empty,
            [],
            explicitSendObserved: false));
        Assert.True(WeChatNewMessageConfirmationPolicy.IsConfirmed(
            string.Empty,
            [],
            explicitSendObserved: true));
    }

    [Fact]
    public void AcceptsNewMessageContainingPastedUrl()
    {
        var urls = UrlExtractor.Extract("https://example.com/path");

        var matched = WeChatNewMessageConfirmationPolicy.IsConfirmed(
            "sent https://example.com/path",
            urls,
            explicitSendObserved: true);

        Assert.True(matched);
    }

    [Fact]
    public void RejectsDifferentMessageAfterPastedUrlWasRemoved()
    {
        var urls = UrlExtractor.Extract("https://example.com/path");

        var matched = WeChatNewMessageConfirmationPolicy.IsConfirmed(
            "다른 메시지",
            urls,
            explicitSendObserved: true);

        Assert.False(matched);
    }

    [Fact]
    public void RejectsEmptyMessageWhenOnlySendInputWasObserved()
    {
        var urls = UrlExtractor.Extract("https://example.com/path");

        var matched = WeChatNewMessageConfirmationPolicy.IsConfirmed(
            string.Empty,
            urls,
            explicitSendObserved: true);

        Assert.False(matched);
    }

    [Fact]
    public void RequiresSameConversationAndBaselineOverlap()
    {
        var baseline = new WeChatAccessibilitySnapshot(
            "session-a",
            new HashSet<string>(["message-1"], StringComparer.Ordinal));

        Assert.True(WeChatConversationMatchPolicy.IsSameConversation(
            baseline,
            "session-a",
            [new WeChatAccessibleMessage("message-1", "old")]));
        Assert.False(WeChatConversationMatchPolicy.IsSameConversation(
            baseline,
            "session-b",
            [new WeChatAccessibleMessage("message-1", "old")]));
        Assert.False(WeChatConversationMatchPolicy.IsSameConversation(
            baseline,
            "session-a",
            [new WeChatAccessibleMessage("message-2", "new")]));
    }
}
