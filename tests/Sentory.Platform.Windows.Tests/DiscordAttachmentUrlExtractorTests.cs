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
}
