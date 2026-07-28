using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class LineConfirmationPolicyTests
{
    [Fact]
    public void AcceptsQtMessageListFocusReportedForVisibleComposer()
    {
        Assert.True(LineComposerFocusPolicy.IsUsable(
            composerVisible: true,
            focusedInsideChatPanel: true,
            focusedClassName: string.Empty,
            focusedIsListItem: true,
            sameProcess: true));
    }

    [Fact]
    public void AcceptsQtListFocusWhenPanelOwnershipIsAmbiguous()
    {
        Assert.True(LineComposerFocusPolicy.IsUsable(
            composerVisible: true,
            focusedInsideChatPanel: false,
            focusedClassName: string.Empty,
            focusedIsListItem: true,
            sameProcess: true));
    }

    [Fact]
    public void RejectsLineSearchFieldFocus()
    {
        Assert.False(LineComposerFocusPolicy.IsUsable(
            composerVisible: true,
            focusedInsideChatPanel: false,
            focusedClassName: "AutoSuggestTextArea",
            focusedIsListItem: false,
            sameProcess: true));
    }

    [Fact]
    public void MatchesNormalizedUrlInNewMessage()
    {
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/path?q=1",
            out var url));

        Assert.True(LineMessageMatchPolicy.ContainsEveryUrl(
            "https://example.com/path?q=1",
            [url]));
    }

    [Fact]
    public void RejectsDifferentUrl()
    {
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/path",
            out var url));

        Assert.False(LineMessageMatchPolicy.ContainsEveryUrl(
            "https://example.net/path",
            [url]));
    }

    [Fact]
    public void AcceptsNewMessageWhenLineHidesItsTextValue()
    {
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/path",
            out var url));

        Assert.True(LineMessageMatchPolicy.HasMatchingSendEvidence(
            string.Empty,
            [url]));
    }

    [Fact]
    public void RejectsDifferentVisibleUrlAsSendEvidence()
    {
        Assert.True(UrlNormalizer.TryNormalize(
            "https://example.com/path",
            out var url));

        Assert.False(LineMessageMatchPolicy.HasMatchingSendEvidence(
            "https://example.net/path",
            [url]));
    }

    [Fact]
    public void ConversationRequiresIdentityAndMessageOverlap()
    {
        var baseline = new LineAccessibilitySnapshot(
            "chat-a",
            new HashSet<string>(["one", "two"]));

        Assert.True(LineConversationMatchPolicy.IsSameConversation(
            baseline,
            "chat-a",
            [
                new LineAccessibleMessage("one", string.Empty),
                new LineAccessibleMessage("two", string.Empty),
                new LineAccessibleMessage("three", string.Empty)
            ]));
        Assert.False(LineConversationMatchPolicy.IsSameConversation(
            baseline,
            "chat-b",
            [
                new LineAccessibleMessage("one", string.Empty),
                new LineAccessibleMessage("two", string.Empty)
            ]));
    }
}
