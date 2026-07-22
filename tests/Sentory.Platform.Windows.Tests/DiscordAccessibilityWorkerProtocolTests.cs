using System.Text.Json;
using Sentory.Platform.Windows.Interop;
using Sentory.Platform.Windows.Runtime;

namespace Sentory.Platform.Windows.Tests;

public sealed class DiscordAccessibilityWorkerProtocolTests
{
    [Fact]
    public void PreservesDraftImageCountAcrossWorkerProtocol()
    {
        var response = new DiscordConfirmationResponse(
            DiscordConfirmationOutcome.Confirmed,
            DateTimeOffset.Parse("2026-07-22T03:00:00Z"),
            ["draft-image-count:2"],
            DraftImageCount: 2);

        var json = JsonSerializer.Serialize(response);
        var roundTrip = JsonSerializer.Deserialize<
            DiscordConfirmationResponse>(json);

        Assert.Equal(2, roundTrip?.DraftImageCount);
    }

    [Fact]
    public void PreservesExpectedDraftImageCountAcrossWorkerProtocol()
    {
        var request = new DiscordConfirmationRequest(
            10,
            20,
            30,
            DiscordConfirmationContentKind.DraftImageInspection,
            [],
            3_000,
            ExpectedDraftImageCount: 2);

        var json = JsonSerializer.Serialize(request);
        var roundTrip = JsonSerializer.Deserialize<
            DiscordConfirmationRequest>(json);

        Assert.Equal(2, roundTrip?.ExpectedDraftImageCount);
    }

    [Fact]
    public void WorkerExceptionSignalIncludesFailureLocationAndHResult()
    {
        Exception captured;
        try
        {
            ThrowDiagnosticArgumentException();
            throw new InvalidOperationException("Expected helper to throw.");
        }
        catch (ArgumentException exception)
        {
            captured = exception;
        }

        var signal = DiscordAccessibilityWorker.CreateExceptionSignal(captured);

        Assert.StartsWith("worker-exception:ArgumentException:", signal);
        Assert.Contains(nameof(ThrowDiagnosticArgumentException), signal);
        Assert.Contains("0x80070057", signal);
    }

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

    private static void ThrowDiagnosticArgumentException()
    {
        throw new ArgumentException("diagnostic test");
    }
}
