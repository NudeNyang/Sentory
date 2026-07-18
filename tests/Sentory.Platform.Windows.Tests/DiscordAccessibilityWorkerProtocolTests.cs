using System.Text.Json;
using Sentory.Platform.Windows.Interop;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordAccessibilityWorkerProtocolTests
{
    [Fact]
    public async Task ProcessesMultipleRequestsUntilInputCloses()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var input = new StringReader(
            $$"""
            {"RequestId":"{{firstId}}","Operation":0,"Request":null}
            {"RequestId":"{{secondId}}","Operation":0,"Request":null}

            """);
        var output = new StringWriter();

        var exitCode = await DiscordAccessibilityWorker.RunAsync(
            input,
            output);

        var lines = output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(0, exitCode);
        Assert.Equal(2, lines.Length);
        Assert.Equal(firstId, ReadRequestId(lines[0]));
        Assert.Equal(secondId, ReadRequestId(lines[1]));
    }

    private static Guid ReadRequestId(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .GetProperty("RequestId")
            .GetGuid();
    }
}
