namespace Sentory.Engine.Bridge.Tests;

public sealed class MessengerAvailabilityProbeTests
{
    [Fact]
    public void RunningProcessesAreMatchedToMessengerSources()
    {
        var runningProcesses = new HashSet<string>(
            ["Discord", "Slack", "Weixin"],
            StringComparer.OrdinalIgnoreCase);

        var detected = MessengerAvailabilityProbe.Detect(
            runningProcesses,
            _ => false);

        Assert.True(detected["Discord"]);
        Assert.False(detected["KakaoTalk"]);
        Assert.True(detected["Slack"]);
        Assert.False(detected["WhatsApp"]);
        Assert.False(detected["Telegram"]);
        Assert.False(detected["Line"]);
        Assert.True(detected["WeChat"]);
    }
}
