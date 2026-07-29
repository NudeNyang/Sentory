using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordAttachmentReplayGuardTests
{
    [Fact]
    public void BlocksRecordedAttachmentWhenHostAndTokenChange()
    {
        const string original =
            "https://cdn.discordapp.com/attachments/1/2/photo.png?ex=old";
        const string rerendered =
            "https://media.discordapp.net/attachments/1/2/photo.png?ex=new";
        const string newlySent =
            "https://cdn.discordapp.com/attachments/1/3/photo.png?ex=new";
        var guard = new DiscordAttachmentReplayGuard(8);

        guard.Record([original]);

        Assert.Empty(guard.SelectUnseen([rerendered]));
        Assert.Equal(newlySent, Assert.Single(guard.SelectUnseen([newlySent])));
    }

    [Fact]
    public void EvictsOldestAttachmentAtCapacity()
    {
        const string first =
            "https://cdn.discordapp.com/attachments/1/1/first.png";
        const string second =
            "https://cdn.discordapp.com/attachments/1/2/second.png";
        var guard = new DiscordAttachmentReplayGuard(1);

        guard.Record([first]);
        guard.Record([second]);

        Assert.Equal(first, Assert.Single(guard.SelectUnseen([first])));
        Assert.Empty(guard.SelectUnseen([second]));
    }
}
