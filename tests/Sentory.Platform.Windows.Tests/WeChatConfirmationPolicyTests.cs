using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class WeChatConfirmationPolicyTests
{
    [Fact]
    public void AcceptsNewMessageContainingPastedUrl()
    {
        var urls = UrlExtractor.Extract("https://example.com/path");

        var matched = WeChatMessageMatchPolicy.HasMatchingSendEvidence(
            "sent https://example.com/path",
            urls,
            preSendComposerMatched: true);

        Assert.True(matched);
    }

    [Fact]
    public void RejectsDifferentMessageAfterPastedUrlWasRemoved()
    {
        var urls = UrlExtractor.Extract("https://example.com/path");

        var matched = WeChatMessageMatchPolicy.HasMatchingComposerEvidence(
            "다른 메시지",
            urls);

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
