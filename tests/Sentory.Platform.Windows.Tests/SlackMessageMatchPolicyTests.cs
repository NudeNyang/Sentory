using Sentory.Core;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class SlackMessageMatchPolicyTests
{
    [Fact]
    public async Task InitialSnapshotRetriesTransientFocusMiss()
    {
        var attempts = 0;
        var expected = new SlackAccessibilitySnapshot(
            "channel",
            "me",
            new HashSet<string> { "message" });

        var actual = await SlackInitialSnapshotRetry.CaptureAsync(
            () => Task.FromResult(
                ++attempts < 3
                    ? null
                    : expected),
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Same(expected, actual);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task InitialSnapshotStopsAfterBoundedRetries()
    {
        var attempts = 0;

        var actual = await SlackInitialSnapshotRetry.CaptureAsync<object>(
            () =>
            {
                attempts++;
                return Task.FromResult<object?>(null);
            },
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Null(actual);
        Assert.Equal(4, attempts);
    }

    [Theory]
    [InlineData("사용자: NudeNyang (누드냥이)")]
    [InlineData("User: NudeNyang (누드냥이)")]
    [InlineData("ユーザー: NudeNyang (누드냥이)")]
    [InlineData("用户：NudeNyang (누드냥이)")]
    public void ParsesCurrentUserFromLocalizedProfileButton(string label)
    {
        Assert.Equal(
            "NudeNyang (누드냥이)",
            SlackMessageMatchPolicy.ParseCurrentUserName(label));
    }

    [Fact]
    public void RequiresKnownSenderUnlessSendKeyWasObserved()
    {
        Assert.True(SlackMessageMatchPolicy.IsOwnMessage(
            "NudeNyang (누드냥이): hello",
            "NudeNyang (누드냥이)",
            explicitSendObserved: false));
        Assert.False(SlackMessageMatchPolicy.IsOwnMessage(
            "Someone: hello",
            "NudeNyang (누드냥이)",
            explicitSendObserved: true));
        Assert.True(SlackMessageMatchPolicy.IsOwnMessage(
            "message without sender",
            null,
            explicitSendObserved: true));
        Assert.False(SlackMessageMatchPolicy.IsOwnMessage(
            "message without sender",
            null,
            explicitSendObserved: false));
    }

    [Fact]
    public void MatchesNormalizedAndDisplayedSlackUrls()
    {
        var urls = new[]
        {
            new NormalizedUrl(
                "https://example.com/path?q=1",
                "https://example.com/path?q=1",
                "example.com")
        };

        Assert.True(SlackMessageMatchPolicy.ContainsEveryUrl(
            "NudeNyang: example.com/path?q=1",
            urls));
        Assert.False(SlackMessageMatchPolicy.ContainsEveryUrl(
            "NudeNyang: example.com/other",
            urls));
    }

    [Fact]
    public void MatchesImageByAccessibilityOrOriginalFileName()
    {
        Assert.True(SlackMessageMatchPolicy.ContainsImage(
            "NudeNyang: sentory-test.png",
            ["sentory-test.png"],
            hasMeaningfulImageElement: false));
        Assert.True(SlackMessageMatchPolicy.ContainsImage(
            "NudeNyang",
            [],
            hasMeaningfulImageElement: true));
        Assert.False(SlackMessageMatchPolicy.ContainsImage(
            "NudeNyang: text only",
            [],
            hasMeaningfulImageElement: false));
    }

    [Fact]
    public void DraftRemovalCancelsOnlyAfterGracePeriod()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-27T00:00:00Z");
        var state = new SlackDraftConfirmationState(
            TimeSpan.FromSeconds(10));

        Assert.False(state.ShouldCancel(
            matchingDraftPresent: true,
            explicitSendObserved: false,
            startedAt));
        Assert.False(state.ShouldCancel(
            matchingDraftPresent: false,
            explicitSendObserved: false,
            startedAt.AddSeconds(1)));
        Assert.False(state.ShouldCancel(
            matchingDraftPresent: false,
            explicitSendObserved: false,
            startedAt.AddSeconds(10)));
        Assert.True(state.ShouldCancel(
            matchingDraftPresent: false,
            explicitSendObserved: false,
            startedAt.AddSeconds(11)));
    }

    [Fact]
    public void SendKeyKeepsRemovedDraftEligibleForConfirmation()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-27T00:00:00Z");
        var state = new SlackDraftConfirmationState(
            TimeSpan.FromSeconds(1));

        Assert.False(state.ShouldCancel(
            matchingDraftPresent: true,
            explicitSendObserved: false,
            startedAt));
        Assert.False(state.ShouldCancel(
            matchingDraftPresent: false,
            explicitSendObserved: true,
            startedAt.AddMinutes(1)));
    }

    [Fact]
    public void ReturningDraftResetsCancellationGracePeriod()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-27T00:00:00Z");
        var state = new SlackDraftConfirmationState(
            TimeSpan.FromSeconds(10));

        Assert.False(state.ShouldCancel(true, false, startedAt));
        Assert.False(state.ShouldCancel(false, false, startedAt.AddSeconds(1)));
        Assert.False(state.ShouldCancel(true, false, startedAt.AddSeconds(9)));
        Assert.False(state.ShouldCancel(false, false, startedAt.AddSeconds(10)));
        Assert.False(state.ShouldCancel(false, false, startedAt.AddSeconds(19)));
        Assert.True(state.ShouldCancel(false, false, startedAt.AddSeconds(20)));
    }

    [Theory]
    [InlineData("파일 제거")]
    [InlineData("Remove file")]
    [InlineData("添付ファイルを削除")]
    [InlineData("移除附件")]
    public void RecognizesLocalizedAttachmentRemoveLabels(string label)
    {
        Assert.True(SlackMessageMatchPolicy.IsAttachmentRemoveLabel(label));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("이모티콘 추가")]
    public void RejectsNonAttachmentButtons(string? label)
    {
        Assert.False(SlackMessageMatchPolicy.IsAttachmentRemoveLabel(label));
    }

    [Fact]
    public void StableMessageIdsOverrideVolatileConversationName()
    {
        var baseline = new SlackAccessibilitySnapshot(
            "old-name",
            "me",
            new HashSet<string> { "one", "two", "three", "four" });
        SlackAccessibleMessage[] current =
        [
            new("one", "", false),
            new("two", "", false),
            new("three", "", false)
        ];

        Assert.True(SlackConversationMatchPolicy.IsSameConversation(
            baseline,
            "new-name",
            current));
    }

    [Fact]
    public void DifferentMessageIdsRejectAnotherConversation()
    {
        var baseline = new SlackAccessibilitySnapshot(
            "same-name",
            "me",
            new HashSet<string> { "one", "two", "three" });
        SlackAccessibleMessage[] current =
        [
            new("other-one", "", false),
            new("other-two", "", false),
            new("other-three", "", false)
        ];

        Assert.False(SlackConversationMatchPolicy.IsSameConversation(
            baseline,
            "same-name",
            current));
    }

    [Fact]
    public void EmptyBaselineFallsBackToConversationIdentity()
    {
        var baseline = new SlackAccessibilitySnapshot(
            "channel",
            "me",
            new HashSet<string>());

        Assert.True(SlackConversationMatchPolicy.IsSameConversation(
            baseline,
            "channel",
            Array.Empty<SlackAccessibleMessage>()));
        Assert.False(SlackConversationMatchPolicy.IsSameConversation(
            baseline,
            "other-channel",
            Array.Empty<SlackAccessibleMessage>()));
    }
}
