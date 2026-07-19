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

    [Fact]
    public async Task KeepsProcessingLongSequenceOfRequests()
    {
        var requestIds = Enumerable.Range(0, 100)
            .Select(_ => Guid.NewGuid())
            .ToList();
        var input = new StringReader(string.Join(
            Environment.NewLine,
            requestIds.Select(requestId =>
                $$"""{"RequestId":"{{requestId}}","Operation":0,"Request":null}""")));
        var output = new StringWriter();

        var exitCode = await DiscordAccessibilityWorker.RunAsync(
            input,
            output);

        var responseIds = output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(ReadRequestId)
            .ToList();
        Assert.Equal(0, exitCode);
        Assert.Equal(requestIds, responseIds);
    }

    private static Guid ReadRequestId(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .GetProperty("RequestId")
            .GetGuid();
    }
}
