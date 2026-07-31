using Sentory.Platform.Windows.Runtime;

namespace Sentory.Engine.Bridge.Tests;

public sealed class DiscordAutomaticRestartPolicyTests
{
    [Fact]
    public void OffersOnlyWhenRunningDiscordIsMissingAccessibilityArgument()
    {
        Assert.True(DiscordAutomaticRestartPolicy.ShouldOffer(
            discordSupportEnabled: true,
            processId: 1234,
            DiscordAccessibilityArgumentState.Missing));
    }

    [Theory]
    [InlineData(false, 1234, DiscordAccessibilityArgumentState.Missing)]
    [InlineData(true, null, DiscordAccessibilityArgumentState.Missing)]
    [InlineData(true, 1234, DiscordAccessibilityArgumentState.Enabled)]
    [InlineData(true, 1234, DiscordAccessibilityArgumentState.Unknown)]
    public void DoesNotOfferForDisabledStoppedPresentOrUnknownDiscord(
        bool enabled,
        int? processId,
        DiscordAccessibilityArgumentState argumentState)
    {
        Assert.False(DiscordAutomaticRestartPolicy.ShouldOffer(
            enabled,
            processId,
            argumentState));
    }
}
