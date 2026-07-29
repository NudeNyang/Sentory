using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordAttachmentUrlExtractorTests
{
    [Fact]
    public void ExtractsOnlyDiscordAttachmentUrlsAndRemovesDuplicates()
    {
        const string attachment =
            "https://cdn.discordapp.com/attachments/123/456/photo.png?ex=abc&hm=def";

        var results = DiscordAttachmentUrlExtractor.Extract(
        [
            $"image {attachment}",
            $"preview: {attachment})",
            "https://example.com/attachments/123/456/photo.png",
            "https://cdn.discordapp.com/not-attachments/photo.png"
        ]);

        Assert.Equal(attachment, Assert.Single(results));
    }

    [Theory]
    [InlineData("http://cdn.discordapp.com/attachments/1/2/a.png")]
    [InlineData("https://discordapp.com/attachments/1/2/a.png")]
    [InlineData("https://cdn.discordapp.com/channels/1/2/a.png")]
    public void RejectsUntrustedUrls(string value)
    {
        Assert.False(DiscordAttachmentUrlExtractor.IsAllowedAttachmentUrl(value));
    }

    [Fact]
    public void SelectsOnlyAttachmentsNotPresentInBaseline()
    {
        const string baseline =
            "https://cdn.discordapp.com/attachments/1/2/old.png?ex=old";
        const string rerendered =
            "https://media.discordapp.net/attachments/1/2/old.png?ex=new";
        const string newlySent =
            "https://cdn.discordapp.com/attachments/1/3/new.png?ex=new";

        var selected = DiscordAttachmentUrlExtractor.SelectNew(
            [rerendered, newlySent],
            [baseline]);

        Assert.Equal(newlySent, Assert.Single(selected));
    }

    [Fact]
    public void SelectsOneUrlPerAttachmentPathWhenTokensDiffer()
    {
        const string first =
            "https://cdn.discordapp.com/attachments/1/2/photo.png?ex=first";
        const string refreshed =
            "https://cdn.discordapp.com/attachments/1/2/photo.png?ex=second";

        var selected = DiscordAttachmentUrlExtractor.SelectNew(
            [first, refreshed],
            []);

        Assert.Equal(first, Assert.Single(selected));
    }
}
